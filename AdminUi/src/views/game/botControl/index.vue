<template>
  <div class="container">
    <Breadcrumb />
    <a-card class="general-card" :title="$t('menu.game.botControl')">
      <div class="status-band">
        <div class="status-item">
          <span>{{ $t('botControl.state.market') }}</span>
          <strong>{{ marketStatusLabel }}</strong>
        </div>
        <div class="status-item">
          <span>{{ $t('botControl.state.active') }}</span>
          <strong>{{ state?.activeBots || 0 }}</strong>
        </div>
        <div class="status-item">
          <span>{{ $t('botControl.state.director') }}</span>
          <strong>{{ state?.marketDirectorBots || 0 }}</strong>
        </div>
        <div class="status-item">
          <span>{{ $t('botControl.state.entrance') }}</span>
          <strong>{{ state?.marketEntranceBots || 0 }}</strong>
        </div>
        <div class="status-item">
          <span>{{ $t('botControl.state.online') }}</span>
          <strong>{{ state?.onlinePlayers || 0 }}</strong>
        </div>
        <a-space>
          <a-button type="primary" :loading="starting" @click="startMarket">
            {{ $t('botControl.action.start') }}
          </a-button>
          <a-tooltip :content="$t('botControl.action.refresh')">
            <a-button :loading="loading" @click="loadAll">
              <template #icon><icon-refresh /></template>
            </a-button>
          </a-tooltip>
        </a-space>
      </div>

      <a-tabs v-model:active-key="activeTab">
        <a-tab-pane key="market" :title="$t('botControl.tab.market')"> </a-tab-pane>
        <a-tab-pane
          key="appearance"
          :title="$t('botControl.tab.appearance')"
        ></a-tab-pane>
        <a-tab-pane
          key="environment"
          :title="$t('botControl.tab.environment')"
        ></a-tab-pane>
        <a-tab-pane key="party" :title="$t('botControl.tab.party')"></a-tab-pane>
      </a-tabs>

      <div v-if="activeTab === 'party'" class="party-panel">
        <div class="party-head">
          <span>{{
            $t('botControl.party.active', { count: party.state.activeTeams })
          }}</span>
          <a-space>
            <a-button
              type="primary"
              size="small"
              :loading="summoning"
              @click="doSummon"
            >
              {{ $t('botControl.party.summon') }}
            </a-button>
            <a-button
              size="small"
              :loading="dismissing"
              :disabled="!party.state.teams.length"
              @click="doDismiss"
            >
              {{ $t('botControl.party.dismiss') }}
            </a-button>
          </a-space>
        </div>
        <div v-if="party.state.teams.length" class="party-list">
          <div
            v-for="team in party.state.teams"
            :key="team.characterId"
            class="party-row"
          >
            <strong>{{ team.name || team.characterId }}</strong>
            <span>{{ $t('botControl.party.map', { map: team.mapId }) }}</span>
            <span>
              {{ $t('botControl.party.companions', { count: team.companions }) }}
            </span>
            <span class="party-remaining">{{
              formatRemaining(team.remainingSec)
            }}</span>
          </div>
        </div>
        <a-empty v-else :description="$t('botControl.party.empty')" />
        <a-alert type="info" class="party-tip">
          {{ $t('botControl.party.tip') }}
        </a-alert>
      </div>

      <a-form layout="vertical">
        <a-row :gutter="16">
          <a-col
            v-for="field in activeFields"
            :key="field.code"
            :xs="24"
            :sm="12"
          >
            <a-form-item :label="fieldLabel(field.code)">
              <a-switch
                v-if="isBoolean(field)"
                :model-value="field.value === 'true'"
                @change="(checked: boolean) => (field.value = String(checked))"
              />
              <a-input-number
                v-else
                v-model="field.numberValue"
                :min="numberRange(field.code).min"
                :max="numberRange(field.code).max"
                hide-button
                @change="
                  (value: number) => (field.value = String(value ?? 1))
                "
              />
            </a-form-item>
          </a-col>
        </a-row>
        <a-button type="primary" :loading="saving" @click="saveActive">
          {{ $t('botControl.action.save') }}
        </a-button>
      </a-form>
    </a-card>
  </div>
</template>

<script lang="ts" setup>
  import { Message } from '@arco-design/web-vue';
  import { computed, onMounted, ref } from 'vue';
  import { useI18n } from 'vue-i18n';
  import {
    BotControlField,
    BotControlState,
    getBotControlConfig,
    getBotControlState,
    getPartyConfig,
    startMarketBots,
    updateBotControlConfig,
    updatePartyConfig,
    summonParty,
    dismissParty,
    PartyConfig,
  } from '@/api/botControl';

  interface EditableField extends BotControlField {
    numberValue?: number;
  }

  const { t } = useI18n();
  const loading = ref(false);
  const saving = ref(false);
  const starting = ref(false);
  const summoning = ref(false);
  const dismissing = ref(false);
  const activeTab = ref('market');
  const state = ref<BotControlState>();
  const marketFields = ref<EditableField[]>([]);
  const appearanceFields = ref<EditableField[]>([]);
  const environmentFields = ref<EditableField[]>([]);
  const partyFields = ref<EditableField[]>([]);
  const party = ref<PartyConfig>({
    config: [],
    state: { teams: [], activeTeams: 0, characterId: 0 },
  });

  const isBoolean = (field: BotControlField) =>
    field.clazz === 'java.lang.Boolean';

  // 组队伴侣的几个数字项范围不一样：数量最多 5（队伍上限 6），时长到 12 小时，等级浮动允许 0
  const numberRange = (code: string) => {
    if (code === 'solo_mapling_party_companion_count')
      return { min: 1, max: 5 };
    if (code === 'solo_mapling_party_companion_minutes')
      return { min: 1, max: 720 };
    if (code === 'solo_mapling_party_companion_level_jitter')
      return { min: 0, max: 10 };
    return { min: 1, max: 60000 };
  };

  const formatRemaining = (seconds: number) => {
    const total = Math.max(0, Math.round(seconds || 0));
    const m = Math.floor(total / 60);
    const s = total % 60;
    return `${m}:${String(s).padStart(2, '0')}`;
  };

  const fieldLabel = (code: string) => {
    const key = `botControl.field.${code}`;
    const label = t(key);
    return label === key ? code : label;
  };

  const toEditable = (fields: BotControlField[] = []) =>
    fields.map((field) => ({
      ...field,
      numberValue: Number(field.value),
    }));

  const activeFields = computed(() => {
    if (activeTab.value === 'appearance') {
      return appearanceFields.value;
    }
    if (activeTab.value === 'environment') {
      return environmentFields.value;
    }
    if (activeTab.value === 'party') {
      return partyFields.value;
    }
    return marketFields.value;
  });

  const marketStatusLabel = computed(() => {
    if (state.value?.marketReady) {
      return t('botControl.state.ready');
    }
    if (state.value?.marketStarting) {
      return t('botControl.state.starting');
    }
    return t('botControl.state.empty');
  });

  const loadAll = async () => {
    loading.value = true;
    try {
      const [stateRes, configRes, partyRes] = await Promise.all([
        getBotControlState(),
        getBotControlConfig(),
        getPartyConfig(),
      ]);
      state.value = stateRes.data;
      marketFields.value = toEditable(configRes.data.market);
      appearanceFields.value = toEditable(configRes.data.appearance);
      environmentFields.value = toEditable(configRes.data.environment);
      partyFields.value = toEditable(
        partyRes.data.config?.length ? partyRes.data.config : configRes.data.party
      );
      party.value = {
        config: partyRes.data.config || [],
        state: partyRes.data.state || {
          teams: [],
          activeTeams: 0,
          characterId: 0,
        },
      };
    } finally {
      loading.value = false;
    }
  };

  const saveAll = async () => {
    saving.value = true;
    try {
      const payload = [
        ...marketFields.value,
        ...appearanceFields.value,
        ...environmentFields.value,
      ].map((field) => ({
        id: field.id,
        code: field.code,
        clazz: field.clazz,
        value: field.value,
        desc: field.desc,
      }));
      const { data } = await updateBotControlConfig(payload);
      Message.success(t('botControl.message.saved', { count: data }));
      await loadAll();
    } finally {
      saving.value = false;
    }
  };

  // 组队的字段是另一组接口（solo_mapling_party_*），别混进 /config 的提交
  const saveActive = async () => {
    if (activeTab.value !== 'party') {
      await saveAll();
      return;
    }
    saving.value = true;
    try {
      const payload = partyFields.value.map((field) => ({
        id: field.id,
        code: field.code,
        clazz: field.clazz,
        value: field.value,
        desc: field.desc,
      }));
      const { data } = await updatePartyConfig(payload);
      Message.success(t('botControl.message.saved', { count: data }));
      await loadAll();
    } finally {
      saving.value = false;
    }
  };

  const doSummon = async () => {
    summoning.value = true;
    try {
      const { data } = await summonParty();
      party.value = { config: party.value.config, state: data };
      Message.success(t('botControl.party.summoned'));
      await loadAll();
    } finally {
      summoning.value = false;
    }
  };

  const doDismiss = async () => {
    dismissing.value = true;
    try {
      const { data } = await dismissParty();
      party.value = { config: party.value.config, state: data };
      Message.success(t('botControl.party.dismissed'));
      await loadAll();
    } finally {
      dismissing.value = false;
    }
  };

  const startMarket = async () => {
    starting.value = true;
    try {
      const { data } = await startMarketBots();
      Message.success(t(`botControl.message.start.${data}`));
      await loadAll();
    } finally {
      starting.value = false;
    }
  };

  onMounted(loadAll);
</script>

<style scoped lang="less">
  .container {
    padding: 0 20px 20px;
  }

  .status-band {
    display: flex;
    flex-wrap: wrap;
    gap: 16px;
    align-items: center;
    margin-bottom: 16px;
  }

  .status-item {
    display: flex;
    flex-direction: column;
    min-width: 120px;

    span {
      color: var(--color-text-3);
      font-size: 12px;
    }
  }

  .party-panel {
    margin-bottom: 16px;
  }

  .party-head {
    display: flex;
    flex-wrap: wrap;
    gap: 12px;
    align-items: center;
    justify-content: space-between;
    margin-bottom: 12px;
    font-weight: 500;
  }

  .party-list {
    margin-bottom: 12px;
    border-radius: 4px;
    background: var(--color-fill-1);
  }

  .party-row {
    display: flex;
    flex-wrap: wrap;
    gap: 16px;
    align-items: center;
    padding: 8px 12px;
    font-size: 13px;

    & + .party-row {
      border-top: 1px solid var(--color-border-1);
    }

    span {
      color: var(--color-text-3);
    }
  }

  .party-remaining {
    margin-left: auto;
    color: var(--color-text-2);
    font-variant-numeric: tabular-nums;
  }

  .party-tip {
    margin-top: 8px;
  }
</style>
