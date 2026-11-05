import axios from 'axios';
import { DropState } from '@/store/modules/drop/type';

export interface DropConditionState {
  dropperId?: number;
  dropperName?: string;
  continent?: number;
  itemId?: number;
  itemName?: string;
  questId?: number;
  pageNo?: number;
  pageSize?: number;
  onlyTotal?: boolean;
  notPage?: boolean;
}

export function getDrop(data: DropConditionState) {
  return axios.post('/drop/v1/getDropList', data);
}

export function updateDrop(data: DropState) {
  return axios.post('/drop/v1/updateDropData', data);
}

export function insertDrop(data: DropState) {
  return axios.put('/drop/v1/addDropData', data);
}

export function deleteDrop(data: DropState) {
  // 基线行没有真实行 id（后端给的是负数合成键），所以把定位用的 (怪物, 道具) 一起带上；
  // 后端按这两个值判断「删的是基线自带掉落（写 chance=0 墓碑）还是纯覆盖行（真删）」。
  return axios.delete(`/drop/v1/deleteDropData/${data.id}`, {
    params: { dropperId: data.dropperId, itemId: data.itemId },
  });
}

export function getGlobalDrop(data: DropConditionState) {
  return axios.post('/drop/v1/getGlobalDropList', data);
}

export function updateGlobalDrop(data: DropState) {
  return axios.post('/drop/v1/updateGlobalDropData', data);
}

export function insertGlobalDrop(data: DropState) {
  return axios.put('/drop/v1/addGlobalDropData', data);
}

export function deleteGlobalDrop(data: DropState) {
  return axios.delete(`/drop/v1/deleteGlobalDropData/${data.id}`, {
    params: { continent: data.continent, itemId: data.itemId },
  });
}
