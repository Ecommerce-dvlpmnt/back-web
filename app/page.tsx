'use client';

import { useState } from 'react';
import CheckoutPage from './checkout/page';
import NovaPoshtaFullPage from './nova-poshta/page';

export default function RootSwitcherPage() {
  const [activeTab, setActiveTab] = useState<'checkout' | 'map'>('checkout');

  return (
    <div className="min-h-screen flex flex-col bg-slate-50 font-sans">
      {/* Верхний переключатель для удобного тестирования обоих режимов */}
      <div className="bg-slate-900 text-white px-4 py-2 flex items-center justify-between text-xs shrink-0 z-40 border-b border-slate-800">
        <div className="flex items-center gap-2">
          <span className="w-2 h-2 rounded-full bg-emerald-400 animate-pulse"></span>
          <span className="font-bold tracking-wide">ДЕМО-СТЕНД ІНТЕГРАЦІЇ НОВОЇ ПОШТИ:</span>
        </div>

        <div className="flex items-center gap-1.5 bg-slate-800 p-1 rounded-xl">
          <button
            type="button"
            onClick={() => setActiveTab('checkout')}
            className={`px-3 py-1 rounded-lg text-xs font-bold transition cursor-pointer flex items-center gap-1.5 ${
              activeTab === 'checkout'
                ? 'bg-red-600 text-white shadow-sm'
                : 'text-slate-400 hover:text-white'
            }`}
          >
            <span>🛒</span>
            <span>Оформлення замовлення (Checkout)</span>
          </button>

          <button
            type="button"
            onClick={() => setActiveTab('map')}
            className={`px-3 py-1 rounded-lg text-xs font-bold transition cursor-pointer flex items-center gap-1.5 ${
              activeTab === 'map'
                ? 'bg-red-600 text-white shadow-sm'
                : 'text-slate-400 hover:text-white'
            }`}
          >
            <span>🗺️</span>
            <span>Повнорозмірна мапа Нової Пошти</span>
          </button>
        </div>
      </div>

      {/* Контент активного режима */}
      <div className="flex-1">
        {activeTab === 'checkout' ? <CheckoutPage /> : <NovaPoshtaFullPage />}
      </div>
    </div>
  );
}
