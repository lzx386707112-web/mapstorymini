import axios from 'axios';

export interface BotControlField {
  id: number;
  code: string;
  clazz: string;
  value: string;
  desc: string;
}

export interface BotControlState {
  activeBots: number;
  marketDirectorBots: number;
  marketEntranceBots: number;
  onlinePlayers: number;
  marketReady: boolean;
  marketStarting: boolean;
}

export interface BotControlConfig {
  market: BotControlField[];
  appearance: BotControlField[];
  environment: BotControlField[];
  party: BotControlField[];
}

export const getBotControlState = () =>
  axios.get<BotControlState>('/bot-control/v1/state');
export const getBotControlConfig = () =>
  axios.get<BotControlConfig>('/bot-control/v1/config');
export const updateBotControlConfig = (fields: BotControlField[]) =>
  axios.put<number>('/bot-control/v1/config', { fields });
export const startMarketBots = () =>
  axios.post<string>('/bot-control/v1/market/start');

// —— 组队伴侣（消耗品 2431159「基友集合」）——
export interface PartyTeam {
  characterId: number;
  name: string;
  mapId: number;
  companions: number;
  remainingSec: number;
}

export interface PartyState {
  teams: PartyTeam[];
  activeTeams: number;
  characterId: number;
}

export interface PartyConfig {
  config: BotControlField[];
  state: PartyState;
}

export const getPartyConfig = () =>
  axios.get<PartyConfig>('/bot-control/v1/party');
export const updatePartyConfig = (fields: BotControlField[]) =>
  axios.put<number>('/bot-control/v1/party', { fields });
export const summonParty = (characterId?: number) =>
  axios.post<PartyState>('/bot-control/v1/party/summon', { characterId });
export const dismissParty = (characterId?: number) =>
  axios.post<PartyState>('/bot-control/v1/party/dismiss', { characterId });
