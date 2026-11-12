'use client';

import { useState } from 'react';
import NovaPoshtaPickerModal from '@/components/NovaPoshtaPickerModal';
import type { Warehouse } from '@/components/NovaPoshtaMap';
import type { City } from '@/app/nova-poshta/page';

interface OrderItem {
  id: string;
  title: string;
  variant: string;
  price: number;
  quantity: number;
  image: string;
}

const SAMPLE_ITEMS: OrderItem[] = [
  {
    id: 'prod-1',
    title: 'Бездротові навушники Pro Sound ANC',
    variant: 'Колір: Чорний матовий',
    price: 2499,
    quantity: 1,
    image: '🎧',
  },
  {
    id: 'prod-2',
    title: 'Захисний протиударний чохол MagSafe',
    variant: 'Для iPhone 15 Pro',
    price: 450,
    quantity: 1,
    image: '📱',
  },
];

export default function CheckoutPage() {
  // Контактные данные
  const [firstName, setFirstName] = useState('Олександр');
  const [lastName, setLastName] = useState('Коваленко');
  const [phone, setPhone] = useState('+380 97 123 45 67');
  const [email, setEmail] = useState('alex.kovalenko@example.com');

  // Доставка
  const [deliveryMethod, setDeliveryMethod] = useState<'nova_poshta_warehouse' | 'courier' | 'pickup'>('nova_poshta_warehouse');
  const [selectedCity, setSelectedCity] = useState<City>({
    Ref: '8d5a980d-391c-11dd-90d9-001a92567626',
    Description: 'Київ',
    AreaDescription: 'Київська',
  });
  const [selectedWarehouse, setSelectedWarehouse] = useState<Warehouse | null>({
    Ref: '1ec09d88-e1c2-11e3-8c4a-0050568002cf',
    Description: 'Відділення №18: вул. Пирогівський шлях, 135',
    Number: '18',
    Latitude: '50.354786',
    Longitude: '30.542884',
    ShortAddress: 'Київ, вул. Пирогівський шлях, 135',
  });

  // Оплата
  const [paymentMethod, setPaymentMethod] = useState<'cod' | 'card'>('cod');
  const [orderComment, setOrderComment] = useState('');

  // Состояние модального окна карты Новой Почты
  const [isMapModalOpen, setIsMapModalOpen] = useState(false);

  // Состояние отправки заказа
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [orderResult, setOrderResult] = useState<{
    orderId: string;
    ttn: string;
    message: string;
  } | null>(null);

  const subtotal = SAMPLE_ITEMS.reduce((sum, item) => sum + item.price * item.quantity, 0);

  // Выбор отделения из карты
  const handleSelectWarehouseFromMap = (warehouse: Warehouse, city: City) => {
    setSelectedCity(city);
    setSelectedWarehouse(warehouse);
  };

  // Отправка заказа на наш бэкенд
  const handleSubmitOrder = async (e: React.FormEvent) => {
    e.preventDefault();

    if (!selectedWarehouse && deliveryMethod === 'nova_poshta_warehouse') {
      alert('Будь ласка, оберіть відділення або поштомат Нової Пошти на карті');
      setIsMapModalOpen(true);
      return;
    }

    setIsSubmitting(true);

    try {
      const response = await fetch('/api/orders', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          customer: {
            firstName,
            lastName,
            phone,
            email,
          },
          delivery: {
            method: deliveryMethod,
            cityRef: selectedCity.Ref,
            cityName: selectedCity.Description,
            warehouseRef: selectedWarehouse?.Ref,
            warehouseName: selectedWarehouse?.Description,
          },
          items: SAMPLE_ITEMS,
          totalAmount: subtotal,
          paymentMethod,
          comment: orderComment,
        }),
      });

      const data = await response.json();

      if (data.success) {
        setOrderResult({
          orderId: data.orderId,
          ttn: data.ttn,
          message: data.message,
        });
      } else {
        alert(data.error || 'Помилка створення замовлення');
      }
    } catch (error) {
      console.error('Помилка замовлення:', error);
      alert('Не вдалося з`єднатися з сервером');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="min-h-screen bg-slate-50 text-slate-800 pb-16">
      {/* Шапка магазина */}
      <header className="bg-white border-b border-slate-200 py-4 px-4 sm:px-8 sticky top-0 z-30 shadow-xs">
        <div className="max-w-6xl mx-auto flex items-center justify-between">
          <div className="flex items-center gap-3">
            <div className="w-9 h-9 bg-slate-900 text-white rounded-xl flex items-center justify-center font-black text-lg">
              S
            </div>
            <div>
              <span className="font-extrabold text-base tracking-tight text-slate-900">
                TECH-STORE
              </span>
              <span className="hidden sm:inline-block text-xs text-slate-400 ml-2">
                | Оформлення замовлення
              </span>
            </div>
          </div>

          <div className="flex items-center gap-2 text-xs font-semibold text-emerald-600 bg-emerald-50 px-3 py-1.5 rounded-lg border border-emerald-200">
            <span>🔒</span>
            <span>Безпечне замовлення</span>
          </div>
        </div>
      </header>

      {/* Основной контент Checkout */}
      <div className="max-w-6xl mx-auto px-4 sm:px-6 pt-6">
        <div className="mb-6 flex items-center justify-between">
          <h1 className="text-2xl sm:text-3xl font-black text-slate-900 tracking-tight">
            Оформлення замовлення
          </h1>
        </div>

        <form onSubmit={handleSubmitOrder} className="grid grid-cols-1 lg:grid-cols-12 gap-8 items-start">
          {/* Левая колонка: Форма данных */}
          <div className="lg:col-span-7 flex flex-col gap-6">
            {/* БЛОК 1: Контактные данные */}
            <section className="bg-white rounded-2xl p-6 border border-slate-200 shadow-xs">
              <div className="flex items-center gap-2.5 mb-4">
                <span className="w-6 h-6 rounded-full bg-slate-900 text-white text-xs font-bold flex items-center justify-center">
                  1
                </span>
                <h2 className="text-base font-bold text-slate-900">Контактні дані покупця</h2>
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                <div>
                  <label className="block text-xs font-semibold text-slate-600 mb-1.5">
                    Ім`я *
                  </label>
                  <input
                    type="text"
                    required
                    value={firstName}
                    onChange={(e) => setFirstName(e.target.value)}
                    className="w-full px-3.5 py-2.5 rounded-xl border border-slate-300 text-sm focus:outline-none focus:ring-2 focus:ring-slate-900 transition"
                  />
                </div>

                <div>
                  <label className="block text-xs font-semibold text-slate-600 mb-1.5">
                    Прізвище *
                  </label>
                  <input
                    type="text"
                    required
                    value={lastName}
                    onChange={(e) => setLastName(e.target.value)}
                    className="w-full px-3.5 py-2.5 rounded-xl border border-slate-300 text-sm focus:outline-none focus:ring-2 focus:ring-slate-900 transition"
                  />
                </div>

                <div>
                  <label className="block text-xs font-semibold text-slate-600 mb-1.5">
                    Номер телефону *
                  </label>
                  <input
                    type="tel"
                    required
                    value={phone}
                    onChange={(e) => setPhone(e.target.value)}
                    className="w-full px-3.5 py-2.5 rounded-xl border border-slate-300 text-sm focus:outline-none focus:ring-2 focus:ring-slate-900 transition font-mono"
                  />
                </div>

                <div>
                  <label className="block text-xs font-semibold text-slate-600 mb-1.5">
                    Email
                  </label>
                  <input
                    type="email"
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    className="w-full px-3.5 py-2.5 rounded-xl border border-slate-300 text-sm focus:outline-none focus:ring-2 focus:ring-slate-900 transition"
                  />
                </div>
              </div>
            </section>

            {/* БЛОК 2: ДОСТАВКА (Интеграция с Новой Почтой) */}
            <section className="bg-white rounded-2xl p-6 border border-slate-200 shadow-xs relative">
              <div className="flex items-center gap-2.5 mb-4">
                <span className="w-6 h-6 rounded-full bg-[#da291c] text-white text-xs font-bold flex items-center justify-center">
                  2
                </span>
                <h2 className="text-base font-bold text-slate-900">Спосіб доставки</h2>
              </div>

              {/* Табы выбора способа доставки */}
              <div className="grid grid-cols-1 sm:grid-cols-3 gap-2 mb-5">
                <div
                  onClick={() => setDeliveryMethod('nova_poshta_warehouse')}
                  className={`p-3.5 rounded-xl border-2 transition cursor-pointer flex flex-col justify-between ${
                    deliveryMethod === 'nova_poshta_warehouse'
                      ? 'border-[#da291c] bg-red-50/40 text-slate-900 shadow-xs'
                      : 'border-slate-200 hover:border-slate-300 bg-white text-slate-600'
                  }`}
                >
                  <div className="flex items-center justify-between mb-2">
                    <span className="font-extrabold text-xs uppercase tracking-wider text-[#da291c]">
                      Нова Пошта
                    </span>
                    <span className={`w-3.5 h-3.5 rounded-full border-2 flex items-center justify-center ${
                      deliveryMethod === 'nova_poshta_warehouse' ? 'border-[#da291c]' : 'border-slate-300'
                    }`}>
                      {deliveryMethod === 'nova_poshta_warehouse' && (
                        <div className="w-1.5 h-1.5 bg-[#da291c] rounded-full"></div>
                      )}
                    </span>
                  </div>
                  <span className="text-xs font-bold">Відділення / Поштомат</span>
                  <span className="text-[11px] text-slate-400 mt-1">від 80 грн • 1-2 дні</span>
                </div>

                <div
                  onClick={() => setDeliveryMethod('courier')}
                  className={`p-3.5 rounded-xl border-2 transition cursor-pointer flex flex-col justify-between ${
                    deliveryMethod === 'courier'
                      ? 'border-[#da291c] bg-red-50/40 text-slate-900 shadow-xs'
                      : 'border-slate-200 hover:border-slate-300 bg-white text-slate-600'
                  }`}
                >
                  <div className="flex items-center justify-between mb-2">
                    <span className="font-extrabold text-xs uppercase tracking-wider text-slate-500">
                      Кур`єр Нова Пошта
                    </span>
                    <span className={`w-3.5 h-3.5 rounded-full border-2 flex items-center justify-center ${
                      deliveryMethod === 'courier' ? 'border-[#da291c]' : 'border-slate-300'
                    }`}>
                      {deliveryMethod === 'courier' && (
                        <div className="w-1.5 h-1.5 bg-[#da291c] rounded-full"></div>
                      )}
                    </span>
                  </div>
                  <span className="text-xs font-bold">Адресна доставка</span>
                  <span className="text-[11px] text-slate-400 mt-1">від 115 грн</span>
                </div>

                <div
                  onClick={() => setDeliveryMethod('pickup')}
                  className={`p-3.5 rounded-xl border-2 transition cursor-pointer flex flex-col justify-between ${
                    deliveryMethod === 'pickup'
                      ? 'border-slate-900 bg-slate-50 text-slate-900 shadow-xs'
                      : 'border-slate-200 hover:border-slate-300 bg-white text-slate-600'
                  }`}
                >
                  <div className="flex items-center justify-between mb-2">
                    <span className="font-extrabold text-xs uppercase tracking-wider text-slate-500">
                      Самовивіз
                    </span>
                    <span className={`w-3.5 h-3.5 rounded-full border-2 flex items-center justify-center ${
                      deliveryMethod === 'pickup' ? 'border-slate-900' : 'border-slate-300'
                    }`}>
                      {deliveryMethod === 'pickup' && (
                        <div className="w-1.5 h-1.5 bg-slate-900 rounded-full"></div>
                      )}
                    </span>
                  </div>
                  <span className="text-xs font-bold">З нашого магазину</span>
                  <span className="text-[11px] text-emerald-600 font-bold mt-1">Безкоштовно</span>
                </div>
              </div>

              {/* КОНТЕНТ ВЫБОРА ОТДЕЛЕНИЯ НОВОЙ ПОЧТЫ */}
              {deliveryMethod === 'nova_poshta_warehouse' && (
                <div className="bg-slate-50 rounded-xl p-4 border border-slate-200 flex flex-col gap-3">
                  <div className="flex items-center justify-between">
                    <span className="text-xs font-semibold text-slate-500 uppercase tracking-wide">
                      Пункт отримання:
                    </span>
                    <span className="text-xs text-slate-500 font-medium">
                      Місто: <strong className="text-slate-800">{selectedCity.Description}</strong>
                    </span>
                  </div>

                  {/* Карточка выбранного отделения или кнопка выбора */}
                  {selectedWarehouse ? (
                    <div className="bg-white rounded-xl p-4 border border-slate-200 shadow-xs flex flex-col sm:flex-row sm:items-center justify-between gap-3">
                      <div className="flex items-start gap-3">
                        <div className="w-9 h-9 rounded-lg bg-red-100 text-[#da291c] font-black text-sm flex items-center justify-center shrink-0 mt-0.5">
                          NP
                        </div>
                        <div>
                          <div className="flex items-center gap-2">
                            <h4 className="font-bold text-sm text-slate-900">
                              {selectedWarehouse.Description}
                            </h4>
                          </div>
                          <p className="text-xs text-slate-500 font-mono mt-0.5">
                            GUID: {selectedWarehouse.Ref}
                          </p>
                        </div>
                      </div>

                      {/* Кнопка смены на интерактивной карте */}
                      <button
                        type="button"
                        onClick={() => setIsMapModalOpen(true)}
                        className="px-4 py-2 rounded-xl text-xs font-bold bg-slate-100 hover:bg-red-50 text-slate-700 hover:text-[#da291c] border border-slate-300 hover:border-red-300 transition shrink-0 cursor-pointer flex items-center justify-center gap-1.5"
                      >
                        <span>🗺️</span>
                        <span>Змінити на мапі</span>
                      </button>
                    </div>
                  ) : (
                    <div className="bg-white rounded-xl p-5 border-2 border-dashed border-red-300 text-center flex flex-col items-center justify-center gap-3">
                      <p className="text-xs text-slate-600 font-medium">
                        Відділення або поштомат ще не обрано
                      </p>
                      <button
                        type="button"
                        onClick={() => setIsMapModalOpen(true)}
                        className="bg-[#da291c] hover:bg-[#b82216] text-white font-bold text-xs py-2.5 px-6 rounded-xl shadow-md transition cursor-pointer flex items-center gap-2"
                      >
                        <span>🗺️</span>
                        <span>Обрати відділення на інтерактивній мапі</span>
                      </button>
                    </div>
                  )}

                  <div className="text-[11px] text-slate-400 flex items-center gap-1.5">
                    <span>💡</span>
                    <span>
                      Ви можете відкрити карту та клікнути на будь-яке відділення або поштомат у вашому місті
                    </span>
                  </div>
                </div>
              )}
            </section>

            {/* БЛОК 3: Способ оплаты */}
            <section className="bg-white rounded-2xl p-6 border border-slate-200 shadow-xs">
              <div className="flex items-center gap-2.5 mb-4">
                <span className="w-6 h-6 rounded-full bg-slate-900 text-white text-xs font-bold flex items-center justify-center">
                  3
                </span>
                <h2 className="text-base font-bold text-slate-900">Спосіб оплати</h2>
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                <label
                  className={`p-3.5 rounded-xl border-2 transition cursor-pointer flex items-center gap-3 ${
                    paymentMethod === 'cod'
                      ? 'border-slate-900 bg-slate-50 font-bold text-slate-900'
                      : 'border-slate-200 text-slate-600 hover:border-slate-300'
                  }`}
                >
                  <input
                    type="radio"
                    name="payment"
                    checked={paymentMethod === 'cod'}
                    onChange={() => setPaymentMethod('cod')}
                    className="accent-slate-900"
                  />
                  <div>
                    <span className="text-xs font-bold block">Накладений платіж</span>
                    <span className="text-[11px] text-slate-400">Оплата при отриманні у відділенні</span>
                  </div>
                </label>

                <label
                  className={`p-3.5 rounded-xl border-2 transition cursor-pointer flex items-center gap-3 ${
                    paymentMethod === 'card'
                      ? 'border-slate-900 bg-slate-50 font-bold text-slate-900'
                      : 'border-slate-200 text-slate-600 hover:border-slate-300'
                  }`}
                >
                  <input
                    type="radio"
                    name="payment"
                    checked={paymentMethod === 'card'}
                    onChange={() => setPaymentMethod('card')}
                    className="accent-slate-900"
                  />
                  <div>
                    <span className="text-xs font-bold block">Оплата карткою онлайн</span>
                    <span className="text-[11px] text-slate-400">Apple Pay, Google Pay, Visa/Mastercard</span>
                  </div>
                </label>
              </div>

              <div className="mt-4">
                <label className="block text-xs font-semibold text-slate-600 mb-1.5">
                  Коментар до замовлення (необов`язково)
                </label>
                <textarea
                  rows={2}
                  value={orderComment}
                  onChange={(e) => setOrderComment(e.target.value)}
                  placeholder="Вкажіть побажання щодо замовлення чи доставки..."
                  className="w-full px-3.5 py-2 rounded-xl border border-slate-300 text-xs focus:outline-none focus:ring-2 focus:ring-slate-900 transition"
                ></textarea>
              </div>
            </section>
          </div>

          {/* Правая колонка: Содержимое заказа и подтверждение */}
          <div className="lg:col-span-5 sticky top-24">
            <div className="bg-white rounded-2xl p-6 border border-slate-200 shadow-xs flex flex-col gap-4">
              <h3 className="font-extrabold text-base text-slate-900 border-b border-slate-100 pb-3">
                Ваше замовлення ({SAMPLE_ITEMS.length})
              </h3>

              {/* Список товаров */}
              <div className="flex flex-col gap-3">
                {SAMPLE_ITEMS.map((item) => (
                  <div key={item.id} className="flex items-center justify-between gap-3 text-xs">
                    <div className="flex items-center gap-2.5">
                      <div className="w-10 h-10 rounded-lg bg-slate-100 flex items-center justify-center text-lg shrink-0">
                        {item.image}
                      </div>
                      <div>
                        <h5 className="font-bold text-slate-800 line-clamp-1">{item.title}</h5>
                        <p className="text-[11px] text-slate-400">{item.variant}</p>
                      </div>
                    </div>
                    <span className="font-bold text-slate-900 whitespace-nowrap">
                      {item.price} грн
                    </span>
                  </div>
                ))}
              </div>

              {/* Расчет стоимости */}
              <div className="border-t border-slate-100 pt-3 flex flex-col gap-2 text-xs">
                <div className="flex justify-between text-slate-500">
                  <span>Вартість товарів:</span>
                  <span className="font-semibold text-slate-700">{subtotal} грн</span>
                </div>

                <div className="flex justify-between text-slate-500">
                  <span>Доставка Новою Поштою:</span>
                  <span className="font-semibold text-slate-700">За тарифами перевізника (~80 грн)</span>
                </div>

                <div className="border-t border-slate-200 pt-3 mt-1 flex justify-between items-baseline">
                  <span className="font-extrabold text-sm text-slate-900">Разом до сплати:</span>
                  <span className="font-black text-xl text-slate-900">{subtotal} грн</span>
                </div>
              </div>

              {/* Главная кнопка оформления */}
              <button
                type="submit"
                disabled={isSubmitting}
                className="w-full bg-[#da291c] hover:bg-[#b82216] disabled:bg-slate-300 text-white font-black py-3.5 px-4 rounded-xl shadow-md transition cursor-pointer text-sm flex items-center justify-center gap-2"
              >
                {isSubmitting ? (
                  <>
                    <div className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin"></div>
                    <span>Створюємо замовлення...</span>
                  </>
                ) : (
                  <span>Підтвердити замовлення • {subtotal} грн</span>
                )}
              </button>

              <p className="text-[10px] text-slate-400 text-center leading-tight">
                Натискаючи кнопку, ви погоджуєтеся з умовами обробки персональних даних та правилами магазину
              </p>
            </div>
          </div>
        </form>
      </div>

      {/* Модальное окно выбора отделения Новой Почты с интерактивной картой */}
      <NovaPoshtaPickerModal
        isOpen={isMapModalOpen}
        initialCity={selectedCity}
        onClose={() => setIsMapModalOpen(false)}
        onSelectWarehouse={handleSelectWarehouseFromMap}
      />

      {/* Модальное окно успешного создания заказа */}
      {orderResult && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/60 backdrop-blur-sm animate-in fade-in">
          <div className="bg-white rounded-2xl max-w-md w-full p-6 text-center border border-slate-200 shadow-2xl flex flex-col items-center">
            <div className="w-14 h-14 rounded-full bg-emerald-100 text-emerald-600 text-2xl flex items-center justify-center mb-3">
              ✓
            </div>

            <h3 className="text-xl font-black text-slate-900 mb-1">
              Дякуємо! Замовлення оформлено
            </h3>
            <p className="text-xs text-slate-500 mb-4">
              Ми вже готуємо ваші товари до відправки
            </p>

            <div className="w-full bg-slate-50 rounded-xl p-4 border border-slate-200 text-left mb-5 flex flex-col gap-2">
              <div className="flex justify-between text-xs">
                <span className="text-slate-400">Номер замовлення:</span>
                <span className="font-extrabold text-slate-900 font-mono">{orderResult.orderId}</span>
              </div>

              <div className="flex justify-between text-xs">
                <span className="text-slate-400">Створена ТТН (Нова Пошта):</span>
                <span className="font-extrabold text-[#da291c] font-mono">{orderResult.ttn}</span>
              </div>

              <div className="flex justify-between text-xs border-t border-slate-200 pt-2 mt-1">
                <span className="text-slate-400">Пункт отримання:</span>
                <span className="font-semibold text-slate-800 text-right line-clamp-2 max-w-[200px]">
                  {selectedWarehouse?.Description}
                </span>
              </div>
            </div>

            <button
              type="button"
              onClick={() => setOrderResult(null)}
              className="w-full bg-slate-900 hover:bg-slate-800 text-white font-bold py-2.5 px-4 rounded-xl text-xs transition cursor-pointer"
            >
              Зрозуміло, продовжити покупки
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
