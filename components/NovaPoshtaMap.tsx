'use client';

import { useEffect, useRef } from 'react';
import { MapContainer, TileLayer, useMap } from 'react-leaflet';
import L from 'leaflet';
import 'leaflet.markercluster';

export interface Warehouse {
  Ref: string;
  Description: string;
  Number: string;
  Latitude: string;
  Longitude: string;
  ShortAddress?: string;
  CategoryOfWarehouse?: string;
  TypeOfWarehouse?: string;
  TotalMaxWeightAllowed?: string;
  GeneratorEnabled?: string | number;
  Schedule?: Record<string, string>;
}

// Фирменная красная иконка для отделений (#da291c)
const npMarkerIcon = L.divIcon({
  className: 'custom-np-pin',
  html: `
    <div style="
      position: relative;
      width: 26px;
      height: 26px;
      background-color: #da291c;
      border: 2px solid #ffffff;
      border-radius: 50% 50% 50% 0;
      transform: rotate(-45deg);
      box-shadow: 0 2px 6px rgba(0,0,0,0.35);
      display: flex;
      align-items: center;
      justify-content: center;
    ">
      <div style="
        width: 8px;
        height: 8px;
        background-color: #ffffff;
        border-radius: 50%;
        transform: rotate(45deg);
      "></div>
    </div>
  `,
  iconSize: [26, 26],
  iconAnchor: [13, 26],
  popupAnchor: [0, -26],
});

// Фирменная бирюзово-зеленая иконка для поштоматов (как на официальном сайте)
const npPostomatIcon = L.divIcon({
  className: 'custom-np-postomat-pin',
  html: `
    <div style="
      position: relative;
      width: 26px;
      height: 26px;
      background-color: #0f8b66;
      border: 2px solid #ffffff;
      border-radius: 50% 50% 50% 0;
      transform: rotate(-45deg);
      box-shadow: 0 2px 6px rgba(0,0,0,0.35);
      display: flex;
      align-items: center;
      justify-content: center;
    ">
      <div style="
        width: 8px;
        height: 8px;
        background-color: #ffffff;
        border-radius: 50%;
        transform: rotate(45deg);
      "></div>
    </div>
  `,
  iconSize: [26, 26],
  iconAnchor: [13, 26],
  popupAnchor: [0, -26],
});

interface MapProps {
  center: [number, number];
  warehouses: Warehouse[];
  selectedWarehouse: Warehouse | null;
  focusedWarehouse: Warehouse | null;
  onSelectWarehouse: (warehouse: Warehouse) => void;
}

// Плавный перелет камеры к выбранному городу
function ChangeMapView({ center }: { center: [number, number] }) {
  const map = useMap();
  useEffect(() => {
    map.flyTo(center, 13, {
      duration: 1.2,
      easeLinearity: 0.25,
    });
  }, [center, map]);
  return null;
}

// Компонент кластеризации маркеров в фирменном стиле Новой Почты (лавандовые кружки с числами)
function MarkerClusterLayer({
  warehouses,
  onSelectWarehouse,
  focusedWarehouse,
}: {
  warehouses: Warehouse[];
  onSelectWarehouse: (w: Warehouse) => void;
  focusedWarehouse: Warehouse | null;
}) {
  const map = useMap();
  const clusterGroupRef = useRef<L.MarkerClusterGroup | null>(null);
  const markersMapRef = useRef<Map<string, L.Marker>>(new Map());

  useEffect(() => {
    if (!map) return;

    // Инициализация группы кластеров
    // Дизайн в точности как на официальном сайте: нежно-лавандовые круги с темно-синими цифрами
    const clusterGroup = (L as any).markerClusterGroup({
      maxClusterRadius: 65,
      showCoverageOnHover: false,
      zoomToBoundsOnClick: true,
      spiderfyOnMaxZoom: true,
      iconCreateFunction: (cluster: any) => {
        const count = cluster.getChildCount();
        let size = 36;
        if (count >= 100) size = 46;
        else if (count >= 50) size = 40;

        return L.divIcon({
          html: `
            <div style="
              width: ${size}px;
              height: ${size}px;
              background-color: rgba(184, 188, 222, 0.90);
              border: 2.5px solid rgba(255, 255, 255, 0.9);
              box-shadow: 0 2px 7px rgba(45, 47, 72, 0.25);
              border-radius: 50%;
              display: flex;
              align-items: center;
              justify-content: center;
              font-family: ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
              font-weight: 700;
              font-size: ${size >= 46 ? '13px' : '12px'};
              color: #1e2038;
            ">
              ${count}
            </div>
          `,
          className: 'custom-np-cluster',
          iconSize: [size, size],
          iconAnchor: [size / 2, size / 2],
        });
      },
    });

    clusterGroupRef.current = clusterGroup;
    markersMapRef.current.clear();

    const validWarehouses = warehouses.filter((wh) => {
      const lat = parseFloat(wh.Latitude);
      const lng = parseFloat(wh.Longitude);
      return !isNaN(lat) && !isNaN(lng) && lat !== 0 && lng !== 0;
    });

    validWarehouses.forEach((wh) => {
      const lat = parseFloat(wh.Latitude);
      const lng = parseFloat(wh.Longitude);

      const isPostomat =
        wh.CategoryOfWarehouse === 'Postomat' ||
        wh.Description.toLowerCase().includes('поштомат');

      const marker = L.marker([lat, lng], {
        icon: isPostomat ? npPostomatIcon : npMarkerIcon,
      });

      // Контент всплывающего окна (Popup)
      const popupContainer = document.createElement('div');
      popupContainer.style.minWidth = '220px';
      popupContainer.style.padding = '4px';

      popupContainer.innerHTML = `
        <div style="display: flex; align-items: center; gap: 6px; font-size: 11px; font-weight: 800; color: ${
          isPostomat ? '#0f8b66' : '#da291c'
        }; text-transform: uppercase; margin-bottom: 4px;">
          <span>№${wh.Number || '—'}</span>
          <span style="color: #cbd5e1;">•</span>
          <span>${isPostomat ? 'Поштомат' : 'Відділення'}</span>
        </div>
        <p style="font-weight: 600; font-size: 13px; color: #1e293b; line-height: 1.35; margin: 0 0 10px 0;">
          ${wh.Description}
        </p>
        <button
          type="button"
          id="btn-select-${wh.Ref}"
          style="
            width: 100%;
            background-color: #da291c;
            color: #ffffff;
            font-weight: 600;
            padding: 7px 12px;
            border-radius: 8px;
            font-size: 12px;
            border: none;
            cursor: pointer;
            box-shadow: 0 1px 3px rgba(0,0,0,0.15);
            transition: background-color 0.15s;
          "
        >
          ✓ Вибрати це відділення
        </button>
      `;

      const btn = popupContainer.querySelector(`#btn-select-${wh.Ref}`);
      if (btn) {
        btn.addEventListener('click', () => {
          onSelectWarehouse(wh);
        });
      }

      marker.bindPopup(popupContainer);
      markersMapRef.current.set(wh.Ref, marker);
      clusterGroup.addLayer(marker);
    });

    map.addLayer(clusterGroup);

    return () => {
      map.removeLayer(clusterGroup);
    };
  }, [map, warehouses, onSelectWarehouse]);

  // Фокусировка на маркере при клике на карточку в левой колонке
  useEffect(() => {
    if (!focusedWarehouse || !map || !clusterGroupRef.current) return;
    const marker = markersMapRef.current.get(focusedWarehouse.Ref);
    if (marker) {
      clusterGroupRef.current.zoomToShowLayer(marker, () => {
        marker.openPopup();
      });
    }
  }, [focusedWarehouse, map]);

  return null;
}

export default function NovaPoshtaMap({
  center,
  warehouses,
  focusedWarehouse,
  onSelectWarehouse,
}: MapProps) {
  return (
    <div className="w-full h-full relative z-0">
      <MapContainer
        center={center}
        zoom={13}
        scrollWheelZoom={true}
        className="w-full h-full"
      >
        <ChangeMapView center={center} />

        <TileLayer
          attribution='&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener">OpenStreetMap</a> contributors'
          url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
        />

        <MarkerClusterLayer
          warehouses={warehouses}
          onSelectWarehouse={onSelectWarehouse}
          focusedWarehouse={focusedWarehouse}
        />
      </MapContainer>
    </div>
  );
}
