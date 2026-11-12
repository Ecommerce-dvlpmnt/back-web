import { NextResponse } from 'next/server';

const API_KEY =
  process.env.NOVA_POSHTA_API_KEY ||
  process.env.NEXT_PUBLIC_NOVA_POSHTA_API_KEY ||
  '8a13126c8d2b835b6670459516bc85cd';
const API_URL = 'https://api.novaposhta.ua/v2.0/json/';

// In-memory кэш для отделений городов
const warehousesCache = new Map<string, { timestamp: number; data: unknown }>();
const CACHE_TTL_MS = 1000 * 60 * 30; // 30 минут

export async function GET(request: Request) {
  const { searchParams } = new URL(request.url);
  const cityRef = searchParams.get('cityRef');

  if (!cityRef) {
    return NextResponse.json(
      { success: false, errors: ['cityRef is required'] },
      { status: 400 }
    );
  }

  // Проверяем серверный кэш (отделения меняются редко, не нужно мучать API Новой Почты)
  const cached = warehousesCache.get(cityRef);
  if (cached && Date.now() - cached.timestamp < CACHE_TTL_MS) {
    return NextResponse.json(cached.data);
  }

  try {
    const response = await fetch(API_URL, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        apiKey: API_KEY,
        modelName: 'Address',
        calledMethod: 'getWarehouses',
        methodProperties: {
          CityRef: cityRef,
        },
      }),
    });

    const data = await response.json();

    if (data.success) {
      warehousesCache.set(cityRef, {
        timestamp: Date.now(),
        data,
      });
    }

    return NextResponse.json(data);
  } catch (error) {
    console.error('[API Warehouses Error]:', error);
    return NextResponse.json(
      { success: false, errors: ['Помилка завантаження відділень'] },
      { status: 500 }
    );
  }
}
