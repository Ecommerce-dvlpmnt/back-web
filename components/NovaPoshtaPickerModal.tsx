'use client';

import { useState, useEffect, useMemo, useRef } from 'react';
import dynamic from 'next/dynamic';
import type { Warehouse } from '@/components/NovaPoshtaMap';
import type { City } from '@/app/nova-poshta/page';

const NovaPoshtaMap = dynamic(() => import('@/components/NovaPoshtaMap'), {
  ssr: false,
  loading: () => (
    <div className="w-full h-full bg-[#f4f5f8] flex flex-col items-center justify-center text-slate-400 font-medium">
      <div className="w-10 h-10 border-4 border-red-500 border-t-transparent rounded-full animate-spin mb-3"></div>
      <span>Завантаження інтерактивної мапи...</span>
    </div>
  ),
});

const POPULAR_CITIES: City[] = [
  { Ref: '8d5a980d-391c-11dd-90d9-001a92567626', Description: 'Київ', AreaDescription: 'Київська' },
  { Ref: 'db5c88e0-391c-11dd-90d9-001a92567626', Description: 'Харків', AreaDescription: 'Харківська' },
  { Ref: 'db5c88d0-391c-11dd-90d9-001a92567626', Description: 'Одеса', AreaDescription: 'Одеська' },
  { Ref: 'db5c88f0-391c-11dd-90d9-001a92567626', Description: 'Дніпро', AreaDescription: 'Дніпропетровська' },
  { Ref: 'db5c88f5-391c-11dd-90d9-001a92567626', Description: 'Львів', AreaDescription: 'Львівська' },
  { Ref: 'db5c88c6-391c-11dd-90d9-001a92567626', Description: 'Запоріжжя', AreaDescription: 'Запорізька' },
  { Ref: 'db5c88de-391c-11dd-90d9-001a92567626', Description: 'Вінниця', AreaDescription: 'Вінницька' },
];

type CategoryFilter = 'all' | 'branch' | 'postomat' | 'point';

interface NovaPoshtaPickerModalProps {
  isOpen: boolean;
  initialCity?: City | null;
  onClose: () => void;
  onSelectWarehouse: (warehouse: Warehouse, city: City) => void;
}

export default function NovaPoshtaPickerModal({
  isOpen,
  initialCity,
  onClose,
  onSelectWarehouse,
}: NovaPoshtaPickerModalProps) {
  const [selectedCity, setSelectedCity] = useState<City>(
    initialCity || POPULAR_CITIES[0] // Киев по умолчанию
  );
  const [searchCity, setSearchCity] = useState(
    initialCity ? initialCity.Description : 'Київ'
  );
  const [cityList, setCityList] = useState<City[]>([]);
  const [isCityDropdownOpen, setIsCityDropdownOpen] = useState(false);
  const [isLoadingCities, setIsLoadingCities] = useState(false);

  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [isLoadingWarehouses, setIsLoadingWarehouses] = useState(false);

  const [filterQuery, setFilterQuery] = useState('');
  const [selectedCategory, setSelectedCategory] = useState<CategoryFilter>('all');

  const [mapCenter, setMapCenter] = useState<[number, number]>([50.4501, 30.5234]);
  const [focusedWarehouse, setFocusedWarehouse] = useState<Warehouse | null>(null);

  const cityDropdownRef = useRef<HTMLDivElement>(null);

  // Синхронизация при открытии
  useEffect(() => {
    if (initialCity && initialCity.Ref !== selectedCity.Ref) {
      setSelectedCity(initialCity);
      setSearchCity(initialCity.Description);
    }
  }, [initialCity]);

  // Загрузка отделений города через наш бэкенд /api/novaposhta/warehouses
  useEffect(() => {
    if (!isOpen || !selectedCity?.Ref) return;

    let isCancelled = false;
    setIsLoadingWarehouses(true);

    fetch(`/api/novaposhta/warehouses?cityRef=${selectedCity.Ref}`)
      .then((res) => res.json())
      .then((data) => {
        if (isCancelled) return;
        if (data.success && Array.isArray(data.data)) {
          const list: Warehouse[] = data.data;
          setWarehouses(list);

          const firstValid = list.find(
            (w) =>
              !isNaN(parseFloat(w.Latitude)) &&
              !isNaN(parseFloat(w.Longitude)) &&
              parseFloat(w.Latitude) !== 0
          );
          if (firstValid) {
            setMapCenter([parseFloat(firstValid.Latitude), parseFloat(firstValid.Longitude)]);
          }
        }
      })
      .catch((err) => console.error('Помилка завантаження відділень:', err))
      .finally(() => {
        if (!isCancelled) setIsLoadingWarehouses(false);
      });

    return () => {
      isCancelled = true;
    };
  }, [isOpen, selectedCity]);

  // Поиск городов через наш защищенный бэкенд /api/novaposhta/cities
  useEffect(() => {
    if (searchCity.trim().length < 2) {
      setCityList([]);
      return;
    }

    const timer = setTimeout(async () => {
      setIsLoadingCities(true);
      try {
        const res = await fetch(`/api/novaposhta/cities?search=${encodeURIComponent(searchCity.trim())}`);
        const data = await res.json();
        if (data.success && Array.isArray(data.data)) {
          setCityList(data.data);
          setIsCityDropdownOpen(true);
        }
      } catch (err) {
        console.error('Помилка пошуку міст:', err);
      } finally {
        setIsLoadingCities(false);
      }
    }, 300);

    return () => clearTimeout(timer);
  }, [searchCity]);

  // Закрытие по клавише ESC
  useEffect(() => {
    function handleKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape') onClose();
    }
    if (isOpen) {
      window.addEventListener('keydown', handleKeyDown);
    }
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [isOpen, onClose]);

  // Фильтрация
  const filteredWarehouses = useMemo(() => {
    return warehouses.filter((wh) => {
      const descLower = wh.Description.toLowerCase();
      const isPostomat = wh.CategoryOfWarehouse === 'Postomat' || descLower.includes('поштомат');
      const isPoint = descLower.includes('пункт');

      if (selectedCategory === 'postomat' && !isPostomat) return false;
      if (selectedCategory === 'branch' && (isPostomat || isPoint)) return false;
      if (selectedCategory === 'point' && !isPoint) return false;

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

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-0 sm:p-4 bg-slate-900/60 backdrop-blur-sm animate-in fade-in duration-200">
      <div className="relative w-full h-full sm:max-w-6xl sm:h-[88vh] bg-white sm:rounded-2xl shadow-2xl flex flex-col overflow-hidden border border-slate-200">
        {/* Шапка модального окна */}
        <div className="h-14 px-5 border-b border-slate-200 flex items-center justify-between bg-white shrink-0">
          <div className="flex items-center gap-3">
            <span className="bg-[#da291c] text-white text-[10px] font-black uppercase px-2 py-0.5 rounded tracking-wider">
              Нова Пошта
            </span>
            <h2 className="text-base font-bold text-slate-800">
              Вибір відділення або поштомату на карті
            </h2>
          </div>

          <button
            type="button"
            onClick={onClose}
            className="w-8 h-8 rounded-full flex items-center justify-center text-slate-400 hover:text-slate-700 hover:bg-slate-100 transition cursor-pointer text-base"
            title="Закрити"
          >
            ✕
          </button>
        </div>

        {/* Тело модального окна: Split интерфейс */}
        <div className="flex flex-1 overflow-hidden relative">
          {/* Левая панель выбора */}
          <div className="w-full sm:w-[380px] lg:w-[420px] h-full bg-white border-r border-slate-200 flex flex-col shrink-0">
            {/* Поиск города */}
            <div className="p-3.5 border-b border-slate-100 flex flex-col gap-2.5">
              <div ref={cityDropdownRef} className="relative">
                <div
                  className="flex items-center border border-slate-300 rounded-xl px-3 py-2 bg-white hover:border-slate-400 transition cursor-pointer"
                  onClick={() => setIsCityDropdownOpen(true)}
                >
                  <span className="text-slate-400 mr-2 text-xs">📍</span>
                  <input
                    type="text"
                    value={searchCity}
                    onChange={(e) => setSearchCity(e.target.value)}
                    onFocus={() => setIsCityDropdownOpen(true)}
                    placeholder="Виберіть місто"
                    className="w-full text-xs font-semibold text-slate-800 outline-none bg-transparent"
                  />
                  {isLoadingCities && (
                    <span className="text-[10px] text-slate-400 animate-pulse">...</span>
                  )}
                </div>

                {/* Выпадающий список городов */}
                {isCityDropdownOpen && (
                  <div className="absolute top-full left-0 right-0 mt-1 bg-white border border-slate-200 rounded-xl shadow-xl max-h-56 overflow-y-auto z-50 divide-y divide-slate-100">
                    {(searchCity.trim().length < 2 ? POPULAR_CITIES : cityList).map((city) => (
                      <div
                        key={city.Ref}
                        onClick={() => {
                          setSelectedCity(city);
                          setSearchCity(city.Description);
                          setIsCityDropdownOpen(false);
                        }}
                        className="px-3 py-2 hover:bg-red-50 cursor-pointer flex justify-between items-center transition text-xs"
                      >
                        <span className="font-semibold text-slate-800">{city.Description}</span>
                        <span className="text-[11px] text-slate-400">
                          {city.AreaDescription ? `${city.AreaDescription} обл.` : ''}
                        </span>
                      </div>
                    ))}
                  </div>
                )}
              </div>

              {/* Поиск внутри отделений */}
              <div className="relative">
                <input
                  type="text"
                  value={filterQuery}
                  onChange={(e) => setFilterQuery(e.target.value)}
                  placeholder="Номер відділення або адреса..."
                  className="w-full pl-8 pr-7 py-1.5 text-xs border border-slate-200 rounded-xl bg-slate-50 focus:bg-white focus:outline-none focus:ring-1 focus:ring-[#da291c]"
                />
                <span className="absolute left-2.5 top-2 text-[11px] text-slate-400">🔍</span>
                {filterQuery && (
                  <button
                    type="button"
                    onClick={() => setFilterQuery('')}
                    className="absolute right-2 top-1.5 text-xs text-slate-400 hover:text-slate-600"
                  >
                    ✕
                  </button>
                )}
              </div>

              {/* Категории */}
              <div className="grid grid-cols-3 gap-1 pt-0.5">
                <button
                  type="button"
                  onClick={() => setSelectedCategory('all')}
                  className={`py-1.5 rounded-lg text-[11px] font-bold transition border cursor-pointer ${
                    selectedCategory === 'all'
                      ? 'bg-red-50 text-[#da291c] border-red-200'
                      : 'bg-white text-slate-600 border-slate-200 hover:bg-slate-50'
                  }`}
                >
                  Всі ({warehouses.length})
                </button>
                <button
                  type="button"
                  onClick={() => setSelectedCategory('branch')}
                  className={`py-1.5 rounded-lg text-[11px] font-bold transition border cursor-pointer ${
                    selectedCategory === 'branch'
                      ? 'bg-red-50 text-[#da291c] border-red-200'
                      : 'bg-white text-slate-600 border-slate-200 hover:bg-slate-50'
                  }`}
                >
                  📦 Відділення
                </button>
                <button
                  type="button"
                  onClick={() => setSelectedCategory('postomat')}
                  className={`py-1.5 rounded-lg text-[11px] font-bold transition border cursor-pointer ${
                    selectedCategory === 'postomat'
                      ? 'bg-emerald-50 text-emerald-700 border-emerald-200'
                      : 'bg-white text-slate-600 border-slate-200 hover:bg-slate-50'
                  }`}
                >
                  📱 Поштомати
                </button>
              </div>
            </div>

            {/* Список карточек */}
            <div className="flex-1 overflow-y-auto divide-y divide-slate-100">
              {isLoadingWarehouses ? (
                <div className="p-6 text-center text-xs text-slate-400 animate-pulse">
                  ⏳ Завантажуємо точки видачі...
                </div>
              ) : filteredWarehouses.length === 0 ? (
                <div className="p-6 text-center text-xs text-slate-400">
                  Нічого не знайдено
                </div>
              ) : (
                filteredWarehouses.slice(0, 100).map((wh) => {
                  const isPostomat =
                    wh.CategoryOfWarehouse === 'Postomat' ||
                    wh.Description.toLowerCase().includes('поштомат');

                  return (
                    <div
                      key={wh.Ref}
                      onClick={() => setFocusedWarehouse(wh)}
                      className="p-3 hover:bg-slate-50 transition cursor-pointer flex flex-col gap-1"
                    >
                      <div className="flex items-center justify-between">
                        <span className="font-bold text-xs text-slate-900 truncate">
                          {isPostomat ? `Поштомат ${wh.Number}` : `Відділення ${wh.Number}`}
                        </span>
                        <span
                          className={`text-[9px] font-extrabold px-1.5 py-0.5 rounded uppercase ${
                            isPostomat
                              ? 'bg-emerald-100 text-emerald-800'
                              : 'bg-red-100 text-[#da291c]'
                          }`}
                        >
                          {isPostomat ? 'Поштомат' : 'Відділення'}
                        </span>
                      </div>

                      <p className="text-[11px] text-slate-600 line-clamp-2">
                        {wh.ShortAddress || wh.Description}
                      </p>

                      <div className="mt-1 flex items-center justify-between pt-0.5">
                        <span className="text-[10px] text-slate-400">
                          📦 До {wh.TotalMaxWeightAllowed || '30'} кг
                        </span>

                        <button
                          type="button"
                          onClick={(e) => {
                            e.stopPropagation();
                            onSelectWarehouse(wh, selectedCity);
                            onClose();
                          }}
                          className="bg-[#da291c] hover:bg-[#b82216] text-white text-[11px] font-bold py-1 px-3 rounded-lg transition shadow-sm cursor-pointer"
                        >
                          Обрати
                        </button>
                      </div>
                    </div>
                  );
                })
              )}
            </div>
          </div>

          {/* Правая часть: Карта с кластеризацией */}
          <div className="flex-1 h-full relative">
            <NovaPoshtaMap
              center={mapCenter}
              warehouses={filteredWarehouses}
              selectedWarehouse={null}
              focusedWarehouse={focusedWarehouse}
              onSelectWarehouse={(wh) => {
                onSelectWarehouse(wh, selectedCity);
                onClose();
              }}
            />
          </div>
        </div>
      </div>
    </div>
  );
}
