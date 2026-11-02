/**
 * 获取icon图片地址
 * @param category 分类： item 或其他
 * @param id 物品id
 * @param location 地区：GMS（默认值）
 * @param version 版本：83（默认值）
 */
export function getIconUrl(
  category: string,
  id: string | number,
  location = 'GMS',
  version = '83'
): string {
  if (!id || id <= 0) return '';
  const apiBase = (import.meta.env.VITE_API_BASE_URL || '').replace(/\/$/, '');
  const kind = category === 'item' && Number(id) < 2000000 ? 'equipment' : category;
  return `${apiBase}/assets/${kind}-icons/${id}.png`;
}

export function getEquipmentPreviewUrl(id: string | number): string {
  const apiBase = (import.meta.env.VITE_API_BASE_URL || '').replace(/\/$/, '');
  return `${apiBase}/assets/equipment-icons/${id}.png`;
}

export function handleEquipmentPreviewError(event: Event, id: number) {
  const image = event.target as HTMLImageElement;
  image.style.visibility = 'hidden';
}

export function nothing() {
  return '占位用';
}
