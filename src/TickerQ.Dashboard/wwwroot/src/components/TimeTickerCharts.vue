<template>
  <div class="charts-container">
    <!-- Chart Toggle Switch - Vertical Edge Design -->
    <div class="chart-toggle-vertical">
      <div class="chart-switch-vertical">
        <div class="toggle-label">Charts</div>
        <v-btn
          :color="activeChart === 'line' ? 'primary' : 'default'"
          variant="elevated"
          density="compact"
          class="chart-btn-vertical"
          @click="activeChart = 'line'"
        >
          <v-icon class="mb-1 chart-icon">mdi-chart-line</v-icon>
          <span class="btn-text">Time</span>
        </v-btn>
        <v-btn
          :color="activeChart === 'pie' ? 'primary' : 'default'"
          variant="elevated"
          density="compact"
          class="chart-btn-vertical"
          @click="activeChart = 'pie'"
        >
          <v-icon class="mb-1 chart-icon">mdi-chart-pie</v-icon>
          <span class="btn-text">Status</span>
        </v-btn>
      </div>
    </div>

    <!-- Time Series Analysis Chart -->
    <div v-if="activeChart === 'line'" class="chart-section">
      <div class="range-controls">
        <div class="range-header">
          <div class="range-info">
            <span class="range-label">Range:</span>
            <span class="range-values">{{ safeRange[0] || -3 }} to {{ safeRange[1] || 3 }} days</span>
          </div>
          <div class="range-actions">
            <v-btn
              icon
              size="x-small"
              variant="text"
              color="primary"
              @click="resetRange"
              class="reset-btn"
              density="compact"
            >
              <v-icon size="x-small">mdi-refresh</v-icon>
            </v-btn>
          </div>
        </div>

        <div class="range-slider-container">
          <v-range-slider
            v-model="safeRange"
            :min="-30"
            :max="30"
            :step="1"
            color="primary"
            track-color="rgba(255, 255, 255, 0.1)"
            thumb-color="primary"
            class="range-slider"
            density="compact"
          />
        </div>
      </div>

      <div class="chart-wrapper">
        <VChart 
          :option="lineChartOptions" 
          :loading="getTimeTickersGraphDataRangeAndParseToGraph?.loading?.value || false"
          class="chart"
        />
      </div>
    </div>

    <!-- Status Distribution Pie Chart -->
    <div v-if="activeChart === 'pie'" class="chart-section">
      <div class="chart-wrapper">
        <VChart 
          :option="pieChartOptions" 
          :loading="getTimeTickersGraphData?.loading?.value || false"
          class="chart"
        />
      </div>
    </div>
  </div>
</template>

<script lang="ts" setup>
import { ref, computed, watch, withDefaults } from 'vue'
import VChart from 'vue-echarts'
import { use } from 'echarts/core'
import { CanvasRenderer } from 'echarts/renderers'
import { LineChart, PieChart } from 'echarts/charts'
import {
  TitleComponent,
  TooltipComponent,
  LegendComponent,
  GridComponent,
  DataZoomComponent,
  ToolboxComponent
} from 'echarts/components'

// Register ECharts components
use([
  CanvasRenderer,
  LineChart,
  PieChart,
  TitleComponent,
  TooltipComponent,
  LegendComponent,
  GridComponent,
  DataZoomComponent,
  ToolboxComponent
])

// Props
interface Props {
  getTimeTickersGraphDataRangeAndParseToGraph: any
  getTimeTickersGraphData: any
  range: number[]
}

const props = withDefaults(defineProps<Props>(), {
  range: () => [-3, 3]
})

// Emits
const emit = defineEmits<{
  'update:range': [value: number[]]
}>()

// Reactive data
const activeChart = ref<'line' | 'pie'>('line')

// Computed properties
const safeRange = computed({
  get: () => props.range || [-3, 3],
  set: (val) => emit('update:range', val)
})

// Chart options
const lineChartOptions = computed(() => ({
  title: {
    text: 'Time Tickers Execution Timeline',
    textStyle: {
      color: '#ffffff',
      fontSize: 16,
      fontWeight: 'bold'
    },
    left: 'center',
    top: 10
  },
  tooltip: {
    trigger: 'axis',
    backgroundColor: 'rgba(0, 0, 0, 0.9)',
    textStyle: {
      color: '#ffffff',
    },
    extraCssText: 'border-radius: 8px; box-shadow: 0 4px 20px rgba(0,0,0,0.3);',
    axisPointer: {
      type: 'cross',
      crossStyle: {
        color: 'rgba(100, 181, 246, 0.8)'
      }
    }
  },
  legend: {
    data: ['Executions'],
    textStyle: {
      color: '#ffffff'
    },
    top: 40
  },
  grid: {
    left: '3%',
    right: '4%',
    bottom: '3%',
    top: 80,
    containLabel: true
  },
  xAxis: {
    type: 'category',
    data: props.getTimeTickersGraphDataRangeAndParseToGraph?.response?.value?.map((item: any) => item.date) || [],
    axisLabel: {
      color: '#ffffff',
      rotate: 45
    },
    axisLine: {
      lineStyle: {
        color: 'rgba(255, 255, 255, 0.3)'
      }
    }
  },
  yAxis: {
    type: 'value',
    axisLabel: {
      color: '#ffffff'
    },
    axisLine: {
      lineStyle: {
        color: 'rgba(255, 255, 255, 0.3)'
      }
    },
    splitLine: {
      lineStyle: {
        color: 'rgba(255, 255, 255, 0.1)'
      }
    }
  },
  series: [
    {
      name: 'Executions',
      type: 'line',
      data: props.getTimeTickersGraphDataRangeAndParseToGraph?.response?.value?.map((item: any) => item.count) || [],
      smooth: true,
      lineStyle: {
        color: '#64b5f6',
        width: 3
      },
      areaStyle: {
        color: {
          type: 'linear',
          x: 0,
          y: 0,
          x2: 0,
          y2: 1,
          colorStops: [
            { offset: 0, color: 'rgba(100, 181, 246, 0.3)' },
            { offset: 1, color: 'rgba(100, 181, 246, 0.05)' }
          ]
        }
      },
      itemStyle: {
        color: '#64b5f6'
      }
    }
  ]
}))

const pieChartOptions = computed(() => ({
  title: {
    text: 'Status Distribution',
    textStyle: {
      color: '#ffffff',
      fontSize: 16,
      fontWeight: 'bold'
    },
    left: 'center',
    top: 10
  },
  tooltip: {
    trigger: 'item',
    backgroundColor: 'rgba(0, 0, 0, 0.9)',
    textStyle: {
      color: '#ffffff',
    },
    extraCssText: 'border-radius: 8px; box-shadow: 0 4px 20px rgba(0,0,0,0.3);'
  },
  legend: {
    orient: 'vertical',
    left: 'left',
    textStyle: {
      color: '#ffffff'
    }
  },
  series: [
    {
      type: 'pie',
      radius: '50%',
      data: props.getTimeTickersGraphData?.response?.value || [],
      emphasis: {
        itemStyle: {
          shadowBlur: 10,
          shadowOffsetX: 0,
          shadowColor: 'rgba(0, 0, 0, 0.5)'
        }
      }
    }
  ]
}))

// Methods
const resetRange = () => {
  emit('update:range', [-7, 7])
}

// Debounce utility
function debounce<T extends (...args: any[]) => void>(fn: T, delay: number): T {
  let timeout: ReturnType<typeof setTimeout>
  return ((...args: any[]) => {
    clearTimeout(timeout)
    timeout = setTimeout(() => fn(...args), delay)
  }) as T
}

// Debounced API call
const fetchGraphData = debounce(async ([min, max]: number[]) => {
  await props.getTimeTickersGraphDataRangeAndParseToGraph?.requestAsync?.(min, max)
}, 200)

// Watch range changes
watch(
  () => props.range.toString(),
  () => {
    fetchGraphData([...props.range])
  },
  {
    immediate: true,
    flush: 'post',
  },
)
</script>

<style scoped>
.charts-container {
  background: rgba(66, 66, 66, 0.9);
  backdrop-filter: blur(20px);
  border-radius: 12px;
  padding: 20px;
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.4);
}

.chart-toggle-vertical {
  position: absolute;
  top: 20px;
  right: 20px;
  z-index: 10;
  background: rgba(0, 0, 0, 0.1);
  border-radius: 20px;
  padding: 8px;
}

.chart-switch-vertical {
  background: rgba(66, 66, 66, 0.95);
  backdrop-filter: blur(20px);
  border-radius: 16px;
  border: 1px solid rgba(100, 181, 246, 0.4);
  box-shadow: 0 8px 32px rgba(100, 181, 246, 0.2);
  padding: 12px;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
}

.toggle-label {
  color: #ffffff;
  font-size: 0.7rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
  border-radius: 12px;
  padding: 10px 6px;
  font-size: 0.75rem;
  margin-bottom: 4px;
}

.chart-btn-vertical {
  min-width: 50px !important;
  width: 50px;
  height: 50px;
  padding: 8px !important;
  border-radius: 12px !important;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 2px;
  transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
  backdrop-filter: blur(10px);
}

.chart-icon {
  font-size: 18px !important;
}

.btn-text {
  font-size: 0.65rem;
  font-weight: 600;
  line-height: 1;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.chart-section {
  position: relative;
  overflow: hidden;
  border-radius: 12px;
  background: rgba(255, 255, 255, 0.02);
  border: 1px solid rgba(255, 255, 255, 0.05);
}

.range-controls {
  background: rgba(0, 0, 0, 0.2);
  border-radius: 6px;
  padding: 4px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  margin-bottom: 16px;
}

.range-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 8px 12px;
  margin-bottom: 8px;
}

.range-info {
  display: flex;
  align-items: center;
  gap: 8px;
}

.range-label {
  color: #ffffff;
  font-size: 0.75rem;
  font-weight: 600;
}

.range-values {
  background: rgba(100, 181, 246, 0.2);
  padding: 1px 4px;
  border-radius: 3px;
  border: 1px solid rgba(100, 181, 246, 0.3);
}

.range-slider-container {
  padding: 0 12px 8px;
}

.chart-wrapper {
  height: 400px;
  width: 100%;
}

.chart {
  height: 100% !important;
  width: 100% !important;
}

@media (max-width: 768px) {
  .chart-toggle-vertical {
    position: static;
    margin-bottom: 16px;
  }
  
  .chart-switch-vertical {
    flex-direction: row;
    padding: 8px;
  }
  
  .chart-btn-vertical {
    width: auto;
    height: 40px;
    min-width: 80px !important;
    flex-direction: row;
    gap: 4px;
  }
}
</style>
