<template>
  <div class="table-skeleton">
    <div class="skeleton-header">
      <div
        v-for="i in columns"
        :key="`header-${i}`"
        class="skeleton-cell skeleton-header-cell"
      >
        <div class="skeleton-bar"></div>
      </div>
    </div>
    <div class="skeleton-body">
      <div
        v-for="row in rows"
        :key="`row-${row}`"
        class="skeleton-row"
      >
        <div
          v-for="col in columns"
          :key="`cell-${row}-${col}`"
          class="skeleton-cell"
        >
          <div 
            class="skeleton-bar"
            :style="{ width: getRandomWidth() }"
          ></div>
        </div>
      </div>
    </div>
  </div>
</template>

<script lang="ts" setup>
interface Props {
  rows?: number
  columns?: number
}

withDefaults(defineProps<Props>(), {
  rows: 5,
  columns: 6
})

const getRandomWidth = () => {
  const widths = ['60%', '70%', '80%', '90%', '100%']
  return widths[Math.floor(Math.random() * widths.length)]
}
</script>

<style scoped>
.table-skeleton {
  width: 100%;
  background: rgba(30, 30, 30, 0.6);
  border-radius: 8px;
  overflow: hidden;
}

.skeleton-header {
  display: flex;
  padding: 12px 0;
  background: rgba(100, 181, 246, 0.1);
  border-bottom: 1px solid rgba(255, 255, 255, 0.1);
}

.skeleton-body {
  padding: 8px 0;
}

.skeleton-row {
  display: flex;
  padding: 8px 0;
  border-bottom: 1px solid rgba(255, 255, 255, 0.05);
}

.skeleton-row:last-child {
  border-bottom: none;
}

.skeleton-cell {
  flex: 1;
  padding: 0 16px;
}

.skeleton-header-cell .skeleton-bar {
  height: 14px;
  width: 70%;
}

.skeleton-bar {
  height: 12px;
  background: linear-gradient(
    90deg,
    rgba(255, 255, 255, 0.1) 0%,
    rgba(255, 255, 255, 0.2) 50%,
    rgba(255, 255, 255, 0.1) 100%
  );
  border-radius: 4px;
  animation: shimmer 1.5s ease-in-out infinite;
  background-size: 200% 100%;
}

@keyframes shimmer {
  0% {
    background-position: -200% 0;
  }
  100% {
    background-position: 200% 0;
  }
}
</style>
