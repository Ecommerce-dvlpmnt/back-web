import { NextResponse } from 'next/server';

interface OrderPayload {
  customer: {
    firstName: string;
    lastName: string;
    phone: string;
    email?: string;
  };
  delivery: {
    method: 'nova_poshta_warehouse' | 'nova_poshta_courier';
    cityRef: string;
    cityName: string;
    warehouseRef?: string;
    warehouseName?: string;
    address?: string;
  };
  items: Array<{
    id: string;
    title: string;
    price: number;
    quantity: number;
  }>;
  totalAmount: number;
}

export async function POST(request: Request) {
  try {
    const body: OrderPayload = await request.json();

    // 1. Валидация заказа на бэкенде
    if (!body.customer?.phone || !body.customer?.firstName) {
      return NextResponse.json(
        { success: false, error: "Будь ласка, заповніть контактні дані" },
        { status: 400 }
      );
    }

    if (body.delivery.method === 'nova_poshta_warehouse' && !body.delivery.warehouseRef) {
      return NextResponse.json(
        { success: false, error: "Будь ласка, оберіть відділення або поштомат Нової Пошти" },
        { status: 400 }
      );
    }

    // 2. Имитация сохранения заказа в базу данных
    const orderId = `NP-${Math.floor(100000 + Math.random() * 900000)}`;

    // 3. Имитация генерации Экспресс-Накладной (ТТН / ЭН) через API Новой Почты (метод InternetDocument.save)
    // В реальном проде здесь вызывается InternetDocument.save с CitySender, Sender, Recipient и т.д.
    const fakeTTN = `20450${Math.floor(100000000 + Math.random() * 900000000)}`;

    console.log('📦 [BACKEND] Отримано нове замовлення:', {
      orderId,
      customer: `${body.customer.firstName} ${body.customer.lastName} (${body.customer.phone})`,
      deliveryCity: body.delivery.cityName,
      warehouse: body.delivery.warehouseName,
      warehouseRef: body.delivery.warehouseRef,
      total: `${body.totalAmount} грн`,
      generatedTTN: fakeTTN,
    });

    return NextResponse.json({
      success: true,
      orderId,
      ttn: fakeTTN,
      message: 'Замовлення успішно створено!',
      deliveryDetails: {
        city: body.delivery.cityName,
        warehouse: body.delivery.warehouseName,
      },
    });
  } catch (error) {
    console.error('[Create Order Error]:', error);
    return NextResponse.json(
      { success: false, error: 'Внутрішня помилка сервера при збереженні замовлення' },
      { status: 500 }
    );
  }
}
