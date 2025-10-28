<template>
  <div class="pagination-footer">
    <div class="pagination-info">
      <span class="text-caption">
        <template v-if="loading">
          <v-progress-circular
            size="12"
            width="2"
            indeterminate
            color="primary"
            class="mr-2"
          ></v-progress-circular>
          Loading...
        </template>
        <template v-else-if="totalCount > 0">
          Showing {{ firstItem }}-{{ lastItem }} of {{ totalCount }} items
        </template>
        <template v-else>
          No items to display
        </template>
      </span>
      
      <v-select
        v-model="localPageSize"
        :items="pageSizeOptions"
        density="compact"
        variant="outlined"
        hide-details
        class="page-size-select ml-3"
        @update:model-value="handlePageSizeChange"
      ></v-select>
    </div>
    
    <v-pagination
      v-model="localPage"
      :length="totalPages"
      :total-visible="7"
      :disabled="props.disabled || props.loading"
      density="compact"
      size="small"
      @update:model-value="handlePageChange"
    ></v-pagination>
  </div>
</template>

<script lang="ts" setup>
import { computed, ref, watch } from 'vue'

interface Props {
  page: number
  pageSize: number
  totalCount: number
  pageSizeOptions?: number[]
  loading?: boolean
  disabled?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  pageSizeOptions: () => [10, 20, 50, 100]
})

const emit = defineEmits<{
  'update:page': [value: number]
  'update:pageSize': [value: number]
}>()

const localPage = ref(props.page)
const localPageSize = ref(props.pageSize)

const totalPages = computed(() => Math.ceil(props.totalCount / localPageSize.value) || 1)
const firstItem = computed(() => ((localPage.value - 1) * localPageSize.value) + 1)
const lastItem = computed(() => Math.min(localPage.value * localPageSize.value, props.totalCount))

watch(() => props.page, (newVal) => {
  localPage.value = newVal
})

watch(() => props.pageSize, (newVal) => {
  localPageSize.value = newVal
})

const handlePageChange = (value: number) => {
  emit('update:page', value)
}

const handlePageSizeChange = (value: number) => {
  // Reset to page 1 when page size changes
  localPage.value = 1
  emit('update:pageSize', value)
  emit('update:page', 1)
}
</script>

<style scoped>
.pagination-footer {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 16px;
  border-top: 1px solid rgba(255, 255, 255, 0.12);
  background: rgba(30, 30, 30, 0.9);
}

.pagination-info {
  display: flex;
  align-items: center;
  gap: 12px;
}

.page-size-select {
  max-width: 100px;
}

:deep(.v-pagination) {
  margin: 0;
}

:deep(.v-pagination__item) {
  min-width: 32px;
  height: 32px;
}

:deep(.v-select__selection) {
  font-size: 0.875rem;
}
</style>
