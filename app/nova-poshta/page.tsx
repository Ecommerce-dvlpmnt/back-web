'use client';

import { useState, useEffect, useRef, useMemo } from 'react';
import dynamic from 'next/dynamic';
import type { Warehouse } from '@/components/NovaPoshtaMap';

// Dynamic import карты без SSR
const NovaPoshtaMap = dynamic(() => import('@/components/NovaPoshtaMap'), {
  ssr: false,
  loading: () => (
    <div className="w-full h-full bg-[#f4f5f8] flex flex-col items-center justify-center text-slate-400 font-medium">
      <div className="w-10 h-10 border-4 border-red-500 border-t-transparent rounded-full animate-spin mb-3"></div>
      <span>Завантаження інтерактивної мапи...</span>
    </div>
  ),
});

const API_KEY =
  process.env.NEXT_PUBLIC_NOVA_POSHTA_API_KEY || '8a13126c8d2b835b6670459516bc85cd';
const API_URL = 'https://api.novaposhta.ua/v2.0/json/';

export interface City {
  Ref: string;
  Description: string;
  AreaDescription: string;
}

const POPULAR_CITIES: City[] = [
  { Ref: '8d5a980d-391c-11dd-90d9-001a92567626', Description: 'Київ', AreaDescription: 'Київська' },
  { Ref: 'db5c88e0-391c-11dd-90d9-001a92567626', Description: 'Харків', AreaDescription: 'Харківська' },
  { Ref: 'db5c88d0-391c-11dd-90d9-001a92567626', Description: 'Одеса', AreaDescription: 'Одеська' },
  { Ref: 'db5c88f0-391c-11dd-90d9-001a92567626', Description: 'Дніпро', AreaDescription: 'Дніпропетровська' },
  { Ref: 'db5c88f5-391c-11dd-90d9-001a92567626', Description: 'Львів', AreaDescription: 'Львівська' },
  { Ref: 'db5c88c6-391c-11dd-90d9-001a92567626', Description: 'Запоріжжя', AreaDescription: 'Запорізька' },
  { Ref: 'db5c88de-391c-11dd-90d9-001a92567626', Description: 'Вінниця', AreaDescription: 'Вінницька' },
  { Ref: 'db5c8892-391c-11dd-90d9-001a92567626', Description: 'Полтава', AreaDescription: 'Полтавська' },
  { Ref: 'db5c8904-391c-11dd-90d9-001a92567626', Description: 'Івано-Франківськ', AreaDescription: 'Івано-Франківська' },
];

type CategoryFilter = 'all' | 'branch' | 'postomat' | 'point';

export default function NovaPoshtaPage() {
  const [searchCity, setSearchCity] = useState('Харків');
  const [cities, setCities] = useState<City[]>([]);
  const [isCityDropdownOpen, setIsCityDropdownOpen] = useState(false);
  const [isLoadingCities, setIsLoadingCities] = useState(false);

  // Выбранный город (по умолчанию сразу подгружаем Харьков, как на скриншоте пользователя)
  const [selectedCity, setSelectedCity] = useState<City | null>(POPULAR_CITIES[1]);
  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [isLoadingWarehouses, setIsLoadingWarehouses] = useState(false);

  // Фильтрация внутри города
  const [filterQuery, setFilterQuery] = useState('');
  const [selectedCategory, setSelectedCategory] = useState<CategoryFilter>('all');

  // Карта
  const [mapCenter, setMapCenter] = useState<[number, number]>([49.9935, 36.2304]); // Харьков
  const [selectedWarehouse, setSelectedWarehouse] = useState<Warehouse | null>(null);
  const [focusedWarehouse, setFocusedWarehouse] = useState<Warehouse | null>(null);

  const cityDropdownRef = useRef<HTMLDivElement>(null);

  // Первичная загрузка отделений Харькова при старте
  useEffect(() => {
    loadWarehousesForCity(POPULAR_CITIES[1]);
  }, []);

  // Клик вне выпадающего списка городов
  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (cityDropdownRef.current && !cityDropdownRef.current.contains(event.target as Node)) {
        setIsCityDropdownOpen(false);
      }
    }
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  // Поиск городов
  useEffect(() => {
    if (searchCity.trim().length < 2) {
      setCities([]);
      return;
    }

    const timer = setTimeout(async () => {
      setIsLoadingCities(true);
      try {
        const response = await fetch(API_URL, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            apiKey: API_KEY,
            modelName: 'Address',
            calledMethod: 'getCities',
            methodProperties: {
              FindByString: searchCity.trim(),
              Limit: '12',
            },
          }),
        });

        const data = await response.json();
        if (data.success && Array.isArray(data.data)) {
          setCities(data.data);
          setIsCityDropdownOpen(true);
        }
      } catch (error) {
        console.error('Помилка пошуку міст:', error);
      } finally {
        setIsLoadingCities(false);
      }
    }, 300);

    return () => clearTimeout(timer);
  }, [searchCity]);

  // Загрузка отделений
  const loadWarehousesForCity = async (city: City) => {
    setSelectedCity(city);
    setSearchCity(city.Description);
    setIsCityDropdownOpen(false);
    setIsLoadingWarehouses(true);
    setFilterQuery('');

    try {
      const response = await fetch(API_URL, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          apiKey: API_KEY,
          modelName: 'Address',
          calledMethod: 'getWarehouses',
          methodProperties: {
            CityRef: city.Ref,
          },
        }),
      });

      const data = await response.json();
      if (data.success && Array.isArray(data.data)) {
        const list: Warehouse[] = data.data;
        setWarehouses(list);

        const firstValid = list.find(
          (w) => !isNaN(parseFloat(w.Latitude)) && !isNaN(parseFloat(w.Longitude)) && parseFloat(w.Latitude) !== 0
        );
        if (firstValid) {
          setMapCenter([parseFloat(firstValid.Latitude), parseFloat(firstValid.Longitude)]);
        }
      }
    } catch (error) {
      console.error('Помилка завантаження відділень:', error);
    } finally {
      setIsLoadingWarehouses(false);
    }
  };

  // Фильтрация отделений по поисковой строке и категории
  const filteredWarehouses = useMemo(() => {
    return warehouses.filter((wh) => {
      // Фильтр по категории
      const descLower = wh.Description.toLowerCase();
      const isPostomat = wh.CategoryOfWarehouse === 'Postomat' || descLower.includes('поштомат');
      const isPoint = descLower.includes('пункт');

      if (selectedCategory === 'postomat' && !isPostomat) return false;
      if (selectedCategory === 'branch' && (isPostomat || isPoint)) return false;
      if (selectedCategory === 'point' && !isPoint) return false;

      // Фильтр по номеру/адресу
      if (filterQuery.trim()) {
        const q = filterQuery.toLowerCase();
        const matchesNumber = wh.Number && wh.Number.toLowerCase().includes(q);
        const matchesDesc = descLower.includes(q);
        const matchesAddress = wh.ShortAddress && wh.ShortAddress.toLowerCase().includes(q);
        return matchesNumber || matchesDesc || matchesAddress;
      }

      return true;
    });
  }, [warehouses, selectedCategory, filterQuery]);

  const handleWarehouseChoose = (wh: Warehouse) => {
    setSelectedWarehouse(wh);
    console.log('✅ Обрано відділення для замовлення:', {
      CityRef: selectedCity?.Ref,
      CityName: selectedCity?.Description,
      WarehouseRef: wh.Ref,
      WarehouseName: wh.Description,
    });
  };

  const isShowingPopular = searchCity.trim().length < 2;
  const displayCities = isShowingPopular ? POPULAR_CITIES : cities;

  return (
    <div className="flex flex-col h-screen w-screen overflow-hidden bg-white text-[#1e2038]">
      {/* 1. Верхний промо-баннер (как на скриншоте Новой Почты) */}
      <div className="bg-[#b3e5d1] text-[#0d5940] text-xs font-semibold py-1.5 px-4 text-center tracking-wide flex items-center justify-center gap-1.5 shrink-0">
        <span>«Легка франшиза» — легко почати СВОЄ</span>
        <span className="text-red-600 font-extrabold">❖</span>
      </div>

      {/* 2. Шапка в стиле официального сайта Новой Почты */}
      <header className="h-16 border-b border-slate-200 px-6 flex items-center justify-between bg-white shrink-0 z-20">
        <div className="flex items-center gap-6">
          {/* Логотип Новая Почта */}
          <div className="flex items-center gap-2 cursor-pointer">
            <span className="text-[#da291c] font-black text-xl tracking-tight uppercase flex items-center gap-1">
              НОВА ПОШТА
            </span>
          </div>

          {/* Меню навигации */}
          <nav className="hidden xl:flex items-center gap-5 text-[13px] font-medium text-slate-600">
            <span className="hover:text-red-600 cursor-pointer">Відправити</span>
            <span className="hover:text-red-600 cursor-pointer">Отримати</span>
            <span className="hover:text-red-600 cursor-pointer">Фінансові послуги</span>
            <span className="hover:text-red-600 cursor-pointer">Міжнародна доставка</span>
            <span className="hover:text-red-600 cursor-pointer">Бізнесу</span>
            <span className="hover:text-red-600 cursor-pointer">Ще</span>
            <span className="hover:text-red-600 cursor-pointer">Вакансії</span>
          </nav>
        </div>

        {/* Правая часть: поиск, язык, вход */}
        <div className="flex items-center gap-4 text-xs font-medium text-slate-600">
          <div className="hidden sm:flex items-center bg-slate-100 rounded-lg px-3 py-1.5 text-slate-400 gap-2 w-56">
            <span>🔍</span>
            <span className="truncate">Відділення, назва послуги...</span>
          </div>
          <span className="cursor-pointer font-bold text-slate-800">УКР</span>
          <div className="flex items-center gap-1.5 border border-slate-300 rounded-lg px-3 py-1.5 cursor-pointer hover:border-slate-400">
            <span>👤</span>
            <span>ВХІД</span>
          </div>
        </div>
      </header>

      {/* 3. Основная рабочая область: Сплит-интерфейс (Левый сайдбар + Карта во весь экран) */}
      <div className="flex flex-1 overflow-hidden relative">
        {/* ЛЕВАЯ ПАНЕЛЬ (Мапа + Фильтры + Список отделений) */}
        <aside className="w-full sm:w-[420px] lg:w-[460px] h-full bg-white border-r border-slate-200 flex flex-col shrink-0 z-10 shadow-lg sm:shadow-none">
          {/* Верхняя часть панели: Заголовок и Выбор города */}
          <div className="p-4 border-b border-slate-100 flex flex-col gap-3 shrink-0">
            <div className="flex items-center justify-between">
              <h1 className="text-xl font-extrabold text-slate-900 tracking-tight">Мапа</h1>
              <span className="text-xs text-slate-400">
                {isLoadingWarehouses ? 'Оновлення...' : `${filteredWarehouses.length} точок`}
              </span>
            </div>

            {/* Выбор города */}
            <div ref={cityDropdownRef} className="relative">
              <div
                className="flex items-center border border-slate-300 rounded-xl px-3 py-2.5 bg-white hover:border-slate-400 transition cursor-pointer"
                onClick={() => setIsCityDropdownOpen(true)}
                onMouseEnter={() => setIsCityDropdownOpen(true)}
              >
                <span className="text-slate-400 mr-2 text-sm">📍</span>
                <input
                  type="text"
                  value={searchCity}
                  onChange={(e) => setSearchCity(e.target.value)}
                  onFocus={() => setIsCityDropdownOpen(true)}
                  placeholder="Виберіть місто"
                  className="w-full text-sm font-semibold text-slate-800 outline-none bg-transparent placeholder-slate-400"
                />
                {isLoadingCities && (
                  <span className="text-[11px] text-slate-400 animate-pulse ml-1">...</span>
                )}
              </div>

              {/* Выпадающий список городов */}
              {isCityDropdownOpen && (
                <div className="absolute top-full left-0 right-0 mt-1 bg-white border border-slate-200 rounded-xl shadow-2xl max-h-64 overflow-y-auto z-50 divide-y divide-slate-100">
                  {isShowingPopular && (
                    <div className="px-3 py-1.5 bg-slate-50 text-[10px] font-bold text-slate-400 uppercase tracking-wider">
                      🔥 Популярні міста
                    </div>
                  )}
                  {displayCities.map((city) => (
                    <div
                      key={city.Ref}
                      onClick={() => loadWarehousesForCity(city)}
                      className="px-3.5 py-2.5 hover:bg-red-50 cursor-pointer flex justify-between items-center transition"
                    >
                      <span className="text-sm font-medium text-slate-800">{city.Description}</span>
                      <span className="text-xs text-slate-400">
                        {city.AreaDescription ? `${city.AreaDescription} обл.` : ''}
                      </span>
                    </div>
                  ))}
                </div>
              )}
            </div>

            {/* Поле поиска внутри выбранного города */}
            <div className="relative">
              <input
                type="text"
                value={filterQuery}
                onChange={(e) => setFilterQuery(e.target.value)}
                placeholder="Відділення/поштомат або їх адреса"
                className="w-full pl-9 pr-8 py-2 text-xs border border-slate-200 rounded-xl bg-slate-50 focus:bg-white focus:outline-none focus:ring-2 focus:ring-[#da291c] transition"
              />
              <span className="absolute left-3 top-2.5 text-xs text-slate-400">🔍</span>
              {filterQuery && (
                <button
                  type="button"
                  onClick={() => setFilterQuery('')}
                  className="absolute right-2.5 top-2 text-xs text-slate-400 hover:text-slate-600"
                >
                  ✕
                </button>
              )}
            </div>

            {/* Фильтры категорий (Всі, Відділення, Поштомати, Пункти) - точь-в-точь как на скриншоте */}
            <div className="grid grid-cols-4 gap-1.5 pt-1">
              <button
                type="button"
                onClick={() => setSelectedCategory('all')}
                className={`py-2 px-1 rounded-xl text-xs font-semibold flex items-center justify-center gap-1 transition cursor-pointer border ${
                  selectedCategory === 'all'
                    ? 'bg-red-50 text-[#da291c] border-red-200'
                    : 'bg-white text-slate-600 border-slate-200 hover:bg-slate-50'
                }`}
              >
                <span>📍</span>
                <span>Всі</span>
              </button>

              <button
                type="button"
                onClick={() => setSelectedCategory('branch')}
                className={`py-2 px-1 rounded-xl text-xs font-semibold flex items-center justify-center gap-1 transition cursor-pointer border ${
                  selectedCategory === 'branch'
                    ? 'bg-red-50 text-[#da291c] border-red-200'
                    : 'bg-white text-slate-600 border-slate-200 hover:bg-slate-50'
                }`}
              >
                <span>📦</span>
                <span>Відділення</span>
              </button>

              <button
                type="button"
                onClick={() => setSelectedCategory('postomat')}
                className={`py-2 px-1 rounded-xl text-xs font-semibold flex items-center justify-center gap-1 transition cursor-pointer border ${
                  selectedCategory === 'postomat'
                    ? 'bg-emerald-50 text-emerald-700 border-emerald-200'
                    : 'bg-white text-slate-600 border-slate-200 hover:bg-slate-50'
                }`}
              >
                <span>📱</span>
                <span>Поштомати</span>
              </button>

              <button
                type="button"
                onClick={() => setSelectedCategory('point')}
                className={`py-2 px-1 rounded-xl text-xs font-semibold flex items-center justify-center gap-1 transition cursor-pointer border ${
                  selectedCategory === 'point'
                    ? 'bg-amber-50 text-amber-700 border-amber-200'
                    : 'bg-white text-slate-600 border-slate-200 hover:bg-slate-50'
                }`}
              >
                <span>🏢</span>
                <span>Пункти</span>
              </button>
            </div>
          </div>

          {/* Список карточек отделений */}
          <div className="flex-1 overflow-y-auto divide-y divide-slate-100">
            <div className="p-3 bg-slate-50/70 text-[11px] font-bold text-slate-400 uppercase tracking-wider flex justify-between items-center sticky top-0 backdrop-blur-sm z-10 border-b border-slate-100">
              <span>{selectedCity?.Description || 'Місто'}: Знайдено {filteredWarehouses.length}</span>
              <span>Клікніть для показу на мапі</span>
            </div>

            {isLoadingWarehouses ? (
              <div className="p-8 text-center text-sm text-slate-400 animate-pulse">
                ⏳ Завантажуємо відділення...
              </div>
            ) : filteredWarehouses.length === 0 ? (
              <div className="p-8 text-center text-sm text-slate-400">
                За вашим запитом нічого не знайдено
              </div>
            ) : (
              filteredWarehouses.slice(0, 150).map((wh) => {
                const isSelected = selectedWarehouse?.Ref === wh.Ref;
                const isPostomat =
                  wh.CategoryOfWarehouse === 'Postomat' ||
                  wh.Description.toLowerCase().includes('поштомат');

                return (
                  <div
                    key={wh.Ref}
                    onClick={() => {
                      setFocusedWarehouse(wh);
                    }}
                    className={`p-4 transition cursor-pointer flex flex-col gap-1.5 hover:bg-slate-50 ${
                      isSelected ? 'bg-red-50/60 border-l-4 border-l-[#da291c]' : ''
                    }`}
                  >
                    <div className="flex items-center justify-between">
                      <span className="font-bold text-sm text-slate-900 leading-snug">
                        {isPostomat ? `Поштомат ${wh.Number || ''}` : `Відділення ${wh.Number || ''}`}
                      </span>

                      <span
                        className={`text-[10px] font-bold px-2 py-0.5 rounded uppercase tracking-wider ${
                          isPostomat
                            ? 'bg-emerald-100 text-emerald-800'
                            : 'bg-red-100 text-[#da291c]'
                        }`}
                      >
                        {isPostomat ? 'Поштомат' : 'Відділення'}
                      </span>
                    </div>

                    {/* Дополнительная плашка характеристик (вес/габариты) */}
                    <div className="text-[11px] text-slate-500 flex items-center gap-2">
                      <span>📦 До {wh.TotalMaxWeightAllowed || '30'} кг</span>
                      <span className="text-slate-300">•</span>
                      <span className="text-emerald-600 font-medium">🟢 Відчинено</span>
                    </div>

                    {/* Точный адрес */}
                    <p className="text-xs text-slate-600 line-clamp-2 mt-0.5">
                      {wh.ShortAddress || wh.Description}
                    </p>

                    {/* Кнопка быстрого выбора прямо в карточке */}
                    <div className="mt-2 flex items-center justify-between pt-1">
                      <span className="text-[10px] text-slate-400 font-mono">
                        Ref: {wh.Ref.slice(0, 8)}...
                      </span>

                      <button
                        type="button"
                        onClick={(e) => {
                          e.stopPropagation();
                          handleWarehouseChoose(wh);
                        }}
                        className={`px-3 py-1 rounded-lg text-xs font-semibold transition cursor-pointer shadow-sm ${
                          isSelected
                            ? 'bg-emerald-600 text-white'
                            : 'bg-[#da291c] hover:bg-[#b82216] text-white'
                        }`}
                      >
                        {isSelected ? '✓ Обрано' : 'Вибрати'}
                      </button>
                    </div>
                  </div>
                );
              })
            )}

            {filteredWarehouses.length > 150 && (
              <div className="p-3 text-center text-xs text-slate-400 bg-slate-50">
                Показано перші 150 відділень із {filteredWarehouses.length}. Скористайтеся пошуком для звуження.
              </div>
            )}
          </div>
        </aside>

        {/* ПРАВАЯ ОБЛАСТЬ: ИНТЕРАКТИВНАЯ КАРТА С КЛАСТЕРИЗАЦИЕЙ (ВО ВЕСЬ ЭКРАН) */}
        <main className="flex-1 h-full relative">
          <NovaPoshtaMap
            center={mapCenter}
            warehouses={filteredWarehouses}
            selectedWarehouse={selectedWarehouse}
            focusedWarehouse={focusedWarehouse}
            onSelectWarehouse={handleWarehouseChoose}
          />

          {/* Плавающий нижний блок подтверждения заказа при выборе отделения */}
          {selectedWarehouse && (
            <div className="absolute bottom-5 left-5 right-5 sm:left-auto sm:right-6 sm:w-[480px] bg-white rounded-2xl shadow-2xl border border-slate-200 p-4 z-50 flex items-center justify-between gap-4 animate-in fade-in slide-in-from-bottom-4 duration-200">
              <div className="min-w-0">
                <span className="text-[10px] uppercase font-bold text-emerald-700 bg-emerald-100 px-2 py-0.5 rounded">
                  ✓ Відділення обрано в замовлення
                </span>
                <h4 className="font-bold text-slate-900 text-sm truncate mt-1">
                  {selectedWarehouse.Description}
                </h4>
                <p className="text-[11px] text-slate-400 font-mono truncate">
                  GUID: {selectedWarehouse.Ref}
                </p>
              </div>

              <button
                type="button"
                onClick={() =>
                  alert(
                    `Готово! Відділення зафіксовано для замовлення:\n${selectedWarehouse.Description}\nGUID: ${selectedWarehouse.Ref}`
                  )
                }
                className="bg-[#da291c] hover:bg-[#b82216] text-white text-xs font-bold py-2.5 px-4 rounded-xl shadow-md transition shrink-0 cursor-pointer"
              >
                Підтвердити
              </button>
            </div>
          )}
        </main>
      </div>
    </div>
  );
}
