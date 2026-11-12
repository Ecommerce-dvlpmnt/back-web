import { NextResponse } from 'next/server';

const API_KEY =
  process.env.NOVA_POSHTA_API_KEY ||
  process.env.NEXT_PUBLIC_NOVA_POSHTA_API_KEY ||
  '8a13126c8d2b835b6670459516bc85cd';
const API_URL = 'https://api.novaposhta.ua/v2.0/json/';

// Бэкенд-прокси для поиска городов.
// Защищает ваш API_KEY (он не попадает в браузер клиентов)
export async function GET(request: Request) {
  const { searchParams } = new URL(request.url);
  const search = searchParams.get('search') || '';

  if (!search.trim()) {
    return NextResponse.json({ success: true, data: [] });
  }

  try {
    const response = await fetch(API_URL, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        apiKey: API_KEY,
        modelName: 'Address',
        calledMethod: 'getCities',
        methodProperties: {
          FindByString: search.trim(),
          Limit: '15',
        },
      }),
      // Кэшируем одинаковые запросы на 1 час (Next.js Data Cache)
      next: { revalidate: 3600 },
    });

    const data = await response.json();
    return NextResponse.json(data);
  } catch (error) {
    console.error('[API Cities Error]:', error);
    return NextResponse.json(
      { success: false, errors: ['Помилка звернення до API Нової Пошти'] },
      { status: 500 }
    );
  }
}
