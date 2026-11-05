export interface DropState {
  id?: number;
  // 覆盖行在 drop_data(_global) 里的行 id；0 = 源端基线自带、还没被后台改过。
  // id 对基线行是负数合成键（后端生成，仅用于列表 row-key / 编辑态），不能拿去查库。
  dbId?: number;
  source?: 'baseline' | 'override';
  dropperId?: number;
  dropperName?: string;
  continent?: number;
  itemId?: number;
  itemName?: string;
  itemCategory?: string;  // 物品类别：装备、消耗、设置、其他、特殊、金币
  minimumQuantity?: number;
  maximumQuantity?: number;
  questId?: number;
  questName?: string;
  chance?: number;
  comments?: string;
}
