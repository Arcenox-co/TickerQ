<template>
  <v-dialog v-model="isOpen" max-width="1400" persistent scrollable>
    <v-card class="chain-modal">
      <!-- Header -->
      <v-card-title class="chain-header">
        <div class="header-content">
          <div class="header-left">
            <v-icon class="header-icon">mdi-family-tree</v-icon>
            <div>
              <h2 class="header-title">Create Chain Jobs</h2>
            </div>
          </div>
          <v-btn
            icon="mdi-close"
            variant="text"
            size="small"
            @click="closeModal"
          />
        </div>
      </v-card-title>

      <v-divider class="header-divider" />

      <!-- Main Content -->
      <v-card-text class="chain-content">
        <v-row no-gutters class="content-row">
          <!-- Left Sidebar - Progress & Example -->
          <v-col cols="12" md="4" class="sidebar">
            <div class="sidebar-inner">
              <!-- Progress Tracker -->
              <div class="progress-tracker">
                <h3 class="tracker-title">
                  <v-icon class="tracker-icon">mdi-progress-check</v-icon>
                  Progress
                </h3>

                <!-- Progress Bar -->
                <div class="progress-bar-section">
                  <v-progress-linear
                    :model-value="progressPercentage"
                    :color="progressPercentage === 100 ? 'success' : 'primary'"
                    height="4"
                    rounded
                    class="progress-bar"
                  />
                </div>

                <!-- Steps List -->
                <div class="steps-container">
                  <div 
                    v-for="(step, index) in steps" 
                    :key="step.id"
                    class="step-item"
                    :class="{
                      'step-active': currentStep === step.id,
                      'step-completed': step.completed,
                      'step-clickable': canNavigateToStep(step.id)
                    }"
                    @click="navigateToStep(step.id)"
                  >
                    <div class="step-indicator">
                      <div class="step-number" v-if="!step.completed && currentStep !== step.id">
                        {{ index + 1 }}
                      </div>
                      <v-icon 
                        v-else
                        :color="getStepIconColor(step)"
                        :icon="step.completed ? 'mdi-check-circle' : 'mdi-circle-outline'"
                        size="16"
                      />
                    </div>
                    <div class="step-content">
                      <div class="step-title">{{ step.title }}</div>
                    </div>
                    <div v-if="index < steps.length - 1" class="step-connector" />
                  </div>
                </div>
              </div>

              <!-- Example Data Panel -->
              <div class="example-data-panel" v-if="currentStep !== 'review'">
                <h3 class="tracker-title">
                  <v-icon class="tracker-icon">mdi-code-json</v-icon>
                  Request Example
                </h3>

                <v-card class="example-card" elevation="2">
                  <v-card-item>
                    <template v-slot:append>
                      <v-btn
                        icon="mdi-content-copy"
                        size="small"
                        variant="text"
                        @click="copyExampleToRequestData"
                        :disabled="!currentExampleData"
                      />
                    </template>
                    <v-card-title class="example-card-title">Example Data</v-card-title>
                    <v-card-subtitle class="example-card-subtitle">{{ currentRequestType }}</v-card-subtitle>
                  </v-card-item>
                  <v-divider />
                  <v-card-text class="example-content">
                    <div v-if="currentExampleData" v-html="formatJsonForDisplay(currentExampleData, true)"></div>
                    <div v-else class="no-example-text">Select a function to see request example</div>
                  </v-card-text>
                </v-card>
              </div>

            </div>
          </v-col>

          <!-- Right Content - Form -->
          <v-col cols="12" md="8" class="form-area">
            <div class="form-inner">
              <!-- Step Header -->
              <div class="step-nav">
                <div class="nav-header">
                  <h3 class="nav-title">{{ getCurrentStepTitle() }}</h3>
                </div>
              </div>

              <!-- Form Content -->
              <div class="form-content">
                <v-window v-model="currentStep" class="step-window">
                  <!-- Step 1: Parent Job -->
                  <v-window-item value="parent" class="step-content">
                    <v-alert 
                      type="info" 
                      variant="tonal" 
                      class="step-alert"
                      icon="mdi-information"
                    >
                      Configure the parent job that will trigger the entire chain
                    </v-alert>

                    <v-form>
                      <v-container>
                        <v-row>
                          <v-col cols="6">
                                  <v-select
                                    v-model="parentJob.functionName"
                                    :items="functionNames"
                                    item-title="functionName"
                                    item-value="functionName"
                                    label="Function Name"
                                    placeholder="Select function"
                                    variant="outlined"
                                    @update:model-value="onFunctionChange('parent', $event)"
                                  />
                          </v-col>
                          <v-col cols="6">
                            <v-text-field
                              v-model="parentJob.description"
                              label="Short description"
                              variant="outlined"
                            />
                          </v-col>
                          <v-col cols="4">
                            <v-text-field
                              v-model.number="parentJob.retries"
                              label="Retries"
                              type="number"
                              min="0"
                              max="10"
                              variant="outlined"
                            />
                          </v-col>
                          <v-col cols="8">
                            <v-combobox
                              v-model="parentJob.retryIntervals"
                              :items="retryIntervalItems"
                              item-title="title"
                              item-value="value"
                              label="Retry intervals (seconds)"
                              placeholder="Select intervals or type custom values"
                              variant="outlined"
                              multiple
                              chips
                              closable-chips
                            />
                          </v-col>
                          <v-col cols="12">
                            <v-textarea
                              v-model="parentJob.requestData"
                              label="Request Data"
                              placeholder='{"key": "value"}'
                              variant="outlined"
                              rows="4"
                            />
                          </v-col>
                          <v-col cols="6" v-if="!parentJob.ignoreDateTime">
                            <v-date-input
                              v-model="parentJob.executionDate"
                              label="Execution Date"
                              variant="outlined"
                              :min="new Date().toISOString().split('T')[0]"
                              prepend-icon=""
                              prepend-inner-icon="$calendar"
                            />
                          </v-col>
                          <v-col cols="6" v-if="!parentJob.ignoreDateTime">
                            <v-text-field
                              v-model="parentJob.executionTime"
                              label="Enter Time (HH:mm:ss)"
                              placeholder="HH:mm:ss"
                              variant="outlined"
                              prepend-icon=""
                              prepend-inner-icon="mdi-clock-outline"
                            />
                          </v-col>
                          <v-col cols="12">
                            <v-checkbox-btn
                              v-model="parentJob.ignoreDateTime"
                              label="Execute immediately (with a one-second delay)"
                            />
                          </v-col>
                        </v-row>
                      </v-container>
                    </v-form>
                  </v-window-item>

                  <!-- Step 2: Children -->
                  <v-window-item value="children" class="step-content">
                    <v-alert 
                      type="info" 
                      variant="tonal" 
                      class="step-alert"
                      icon="mdi-information"
                    >
                      Configure child jobs and their grandchildren (up to 5 children, each with up to 5 grandchildren)
                    </v-alert>

                    <div class="children-container">
                      <!-- Children Header with Add/Remove -->
                      <div class="children-header">
                        <div class="children-title">
                          <h4>Children ({{ children.length }}/5)</h4>
                        </div>
                        <div class="children-actions">
                          <v-btn
                            v-if="children.length < 5"
                            color="primary"
                            variant="outlined"
                            size="small"
                            prepend-icon="mdi-plus"
                            @click="addChild"
                          >
                            Add Child
                          </v-btn>
                        </div>
                      </div>

                      <!-- Children Tabs (if any exist) -->
                      <v-tabs 
                        v-if="children.length > 0"
                        v-model="activeChildIndex" 
                        color="primary"
                        class="children-tabs"
                        show-arrows
                      >
                        <v-tab 
                          v-for="(child, index) in children" 
                          :key="index"
                          :value="index"
                          class="child-tab"
                        >
                          <v-icon class="tab-icon">mdi-account</v-icon>
                          Child {{ index + 1 }}
                          <v-chip 
                            v-if="child.functionName"
                            color="success"
                            size="x-small"
                            class="tab-indicator"
                          >
                            ✓
                          </v-chip>
                          <v-btn
                            icon="mdi-close"
                            variant="text"
                            size="x-small"
                            class="ml-1"
                            @click.stop="removeChild(index)"
                          />
                        </v-tab>
                      </v-tabs>

                      <!-- No Children Message -->
                      <div v-if="children.length === 0" class="no-children-message">
                        <v-alert
                          type="info"
                          variant="tonal"
                          class="text-center"
                        >
                          <div class="d-flex flex-column align-center">
                            <v-icon size="48" class="mb-2">mdi-account-plus</v-icon>
                            <div>No children added yet</div>
                            <div class="text-caption">Click "Add Child" to create your first child job</div>
                          </div>
                        </v-alert>
                      </div>

                      <!-- Children Content -->
                      <v-window 
                        v-if="children.length > 0"
                        v-model="activeChildIndex" 
                        class="children-window"
                      >
                        <v-window-item 
                          v-for="(child, childIndex) in children" 
                          :key="childIndex"
                          :value="childIndex"
                          class="child-content"
                        >
                          <!-- Child Configuration -->
                          <v-form>
                            <v-container>
                              <v-row>
                                <v-col cols="6">
                                  <v-select
                                    v-model="child.functionName"
                                    :items="functionNames"
                                    item-title="functionName"
                                    item-value="functionName"
                                    label="Function Name"
                                    placeholder="Select function"
                                    variant="outlined"
                                    @update:model-value="onFunctionChange('child', $event, childIndex)"
                                  />
                                </v-col>
                                <v-col cols="6">
                                  <v-text-field
                                    v-model="child.description"
                                    label="Short description"
                                    variant="outlined"
                                  />
                                </v-col>
                                <v-col cols="4">
                                  <v-text-field
                                    v-model.number="child.retries"
                                    label="Retries"
                                    type="number"
                                    min="0"
                                    max="10"
                                    variant="outlined"
                                  />
                                </v-col>
                                <v-col cols="8">
                                  <v-combobox
                                    v-model="child.retryIntervals"
                                    :items="retryIntervalItems"
                                    item-title="title"
                                    item-value="value"
                                    label="Retry intervals (seconds)"
                                    placeholder="Select intervals or type custom values"
                                    variant="outlined"
                                    multiple
                                    chips
                                    closable-chips
                                  />
                                </v-col>
                                <v-col cols="12">
                                  <v-textarea
                                    v-model="child.requestData"
                                    label="Request Data"
                                    placeholder='{"key": "value"}'
                                    variant="outlined"
                                    rows="4"
                                  />
                                </v-col>
                                <v-col cols="6" v-if="!child.ignoreDateTime">
                                  <v-date-input
                                    v-model="child.executionDate"
                                    label="Execution Date"
                                    variant="outlined"
                                    :min="new Date().toISOString().split('T')[0]"
                                    prepend-icon=""
                                    prepend-inner-icon="$calendar"
                                  />
                                </v-col>
                                <v-col cols="6" v-if="!child.ignoreDateTime">
                                  <v-text-field
                                    v-model="child.executionTime"
                                    label="Enter Time (HH:mm:ss)"
                                    placeholder="HH:mm:ss"
                                    variant="outlined"
                                    prepend-icon=""
                                    prepend-inner-icon="mdi-clock-outline"
                                  />
                                </v-col>
                                <v-col cols="12">
                                  <v-checkbox-btn
                                    v-model="child.ignoreDateTime"
                                    label="Execute immediately (with a one-second delay)"
                                  />
                                </v-col>
                                <v-col cols="6">
                                  <v-select
                                    v-model="child.runCondition"
                                    :items="runConditions"
                                    label="Run Condition"
                                    placeholder="Select condition"
                                    variant="outlined"
                                  />
                                </v-col>
                                <v-col cols="6" v-if="child.functionName">
                                  <v-btn
                                    color="secondary"
                                    variant="outlined"
                                    prepend-icon="mdi-account-group"
                                    @click="openGrandchildrenModal(childIndex)"
                                    block
                                  >
                                    Manage Grandchildren ({{ getGrandchildrenCount(childIndex) }}/5)
                                  </v-btn>
                                </v-col>
                              </v-row>
                            </v-container>
                          </v-form>
                        </v-window-item>
                      </v-window>
                    </div>
                  </v-window-item>

                  <!-- Step 3: Review -->
                  <v-window-item value="review" class="step-content">
                    <v-alert 
                      type="success" 
                      variant="tonal" 
                      class="step-alert"
                      icon="mdi-check-circle"
                    >
                      Review your chain configuration before creating
                    </v-alert>

                    <div class="review-content">
                      <!-- Parent Summary -->
                      <div class="review-section mb-6">
                        <h4 class="mb-3">
                          <v-icon class="mr-2">mdi-crown</v-icon>
                          Parent Job
                        </h4>
                        <div class="review-details">
                          <div class="review-item">
                            <strong>Function:</strong> {{ parentJob.functionName || 'Not selected' }}
                          </div>
                          <div class="review-item">
                            <strong>Description:</strong> {{ parentJob.description || 'No description' }}
                          </div>
                          <div class="review-item">
                            <strong>Execution:</strong> 
                            {{ parentJob.ignoreDateTime ? 'Immediate' : formatExecutionTime(parentJob) }}
                          </div>
                          <div class="review-item">
                            <strong>Retries:</strong> {{ parentJob.retries }}
                          </div>
                          <div class="review-item" v-if="parentJob.retryIntervals && parentJob.retryIntervals.length > 0">
                            <strong>Retry Intervals:</strong> {{ parentJob.retryIntervals.map(item => typeof item === 'object' ? item.value : item).join(', ') }} seconds
                          </div>
                        </div>
                      </div>

                      <!-- Children Summary -->
                      <div class="review-section">
                        <h4 class="mb-3">
                          <v-icon class="mr-2">mdi-account-group</v-icon>
                          Children ({{ getConfiguredChildrenCount() }}/5)
                        </h4>
                        <div v-if="getConfiguredChildrenCount() === 0" class="empty-state">
                          No children configured
                        </div>
                        <div v-else class="review-details">
                          <div 
                            v-for="(child, index) in getConfiguredChildren()" 
                            :key="index"
                            class="child-summary mb-4"
                          >
                            <div class="child-header mb-2">
                              <v-icon class="child-icon mr-2">mdi-account</v-icon>
                              <strong>Child {{ child.index + 1 }}: {{ child.functionName }}</strong>
                              <v-chip 
                                v-if="child.grandChildrenCount > 0"
                                color="secondary"
                                size="small"
                                variant="tonal"
                                class="ml-2"
                              >
                                {{ child.grandChildrenCount }} grandchildren
                              </v-chip>
                            </div>
                            <div class="child-details ml-6">
                              <div><strong>Condition:</strong> {{ child.runCondition || 'None' }}</div>
                              <div><strong>Description:</strong> {{ child.description || 'No description' }}</div>
                              <div><strong>Retries:</strong> {{ child.retries }}</div>
                            </div>
                          </div>
                        </div>
                      </div>
                    </div>
                  </v-window-item>
                </v-window>
              </div>

              <!-- Bottom Navigation -->
              <div class="bottom-nav">
                <v-btn
                  v-if="canGoBack()"
                  variant="outlined"
                  prepend-icon="mdi-arrow-left"
                  @click="goBack"
                >
                  Back
                </v-btn>
                <v-spacer />
                <v-btn
                  v-if="currentStep !== 'review'"
                  :color="canGoNext() ? 'primary' : 'grey'"
                  variant="elevated"
                  append-icon="mdi-arrow-right"
                  :disabled="!canGoNext()"
                  @click="goNext"
                >
                  Next
                </v-btn>
                <v-btn
                  v-if="currentStep === 'review'"
                  color="success"
                  variant="elevated"
                  prepend-icon="mdi-check"
                  @click="createChainJobs"
                  :loading="isCreating"
                >
                  Create Chain Jobs
                </v-btn>
              </div>
            </div>
          </v-col>
        </v-row>
      </v-card-text>
    </v-card>
  </v-dialog>

  <!-- Grandchildren Modal -->
  <v-dialog v-model="grandchildrenModal.isOpen" max-width="900" persistent scrollable>
    <v-card class="grandchildren-modal">
      <v-card-title class="d-flex align-center pa-4 flex-shrink-0">
        <v-icon class="mr-2" color="secondary">mdi-account-group</v-icon>
        Manage Grandchildren for Child {{ grandchildrenModal.parentChildIndex + 1 }}
        <v-spacer />
        <v-btn
          icon="mdi-close"
          variant="text"
          size="small"
          @click="grandchildrenModal.isOpen = false"
        />
      </v-card-title>
      
      <v-divider class="flex-shrink-0" />
      
      <v-card-text class="grandchildren-content">
        <!-- Grandchildren Header with Add/Remove -->
        <div class="grandchildren-header">
          <div class="grandchildren-title">
            <h5>Grandchildren ({{ children[grandchildrenModal.parentChildIndex]?.grandChildren?.length || 0 }}/5)</h5>
          </div>
          <div class="grandchildren-actions">
            <v-btn
              v-if="(children[grandchildrenModal.parentChildIndex]?.grandChildren?.length || 0) < 5"
              color="secondary"
              variant="outlined"
              size="small"
              prepend-icon="mdi-plus"
              @click="addGrandChild(grandchildrenModal.parentChildIndex)"
            >
              Add Grandchild
            </v-btn>
          </div>
        </div>

        <!-- No Grandchildren Message -->
        <div v-if="!children[grandchildrenModal.parentChildIndex]?.grandChildren?.length" class="no-grandchildren-message">
          <v-alert
            type="info"
            variant="tonal"
            class="text-center ma-4"
          >
            <div class="d-flex flex-column align-center">
              <v-icon size="48" class="mb-2">mdi-account-child-circle</v-icon>
              <div>No grandchildren added yet</div>
              <div class="text-caption">Click "Add Grandchild" to create your first grandchild job</div>
            </div>
          </v-alert>
        </div>

        <!-- Grandchildren Tabs -->
        <v-tabs 
          v-if="children[grandchildrenModal.parentChildIndex]?.grandChildren?.length > 0"
          v-model="activeGrandChildIndexes[grandchildrenModal.parentChildIndex]" 
          color="secondary"
          class="grandchildren-tabs"
          show-arrows
        >
          <v-tab 
            v-for="(grandChild, grandIndex) in children[grandchildrenModal.parentChildIndex]?.grandChildren || []" 
            :key="grandIndex"
            :value="grandIndex"
            class="grandchild-tab"
          >
            <v-icon class="tab-icon mr-1">mdi-account-child</v-icon>
            Grandchild {{ grandIndex + 1 }}
            <v-chip 
              v-if="grandChild.functionName"
              color="success"
              size="x-small"
              class="ml-1"
            >
              ✓
            </v-chip>
            <v-btn
              icon="mdi-close"
              variant="text"
              size="x-small"
              class="ml-1"
              @click.stop="removeGrandChild(grandchildrenModal.parentChildIndex, grandIndex)"
            />
          </v-tab>
        </v-tabs>

        <v-window 
          v-if="children[grandchildrenModal.parentChildIndex]?.grandChildren?.length > 0"
          v-model="activeGrandChildIndexes[grandchildrenModal.parentChildIndex]" 
          class="grandchildren-window"
        >
          <v-window-item 
            v-for="(grandChild, grandIndex) in children[grandchildrenModal.parentChildIndex]?.grandChildren || []" 
            :key="grandIndex"
            :value="grandIndex"
            class="grandchild-content"
          >
            <v-form>
              <v-container>
                <v-row>
                  <v-col cols="6">
                    <v-select
                      v-model="grandChild.functionName"
                      :items="functionNames"
                      item-title="functionName"
                      item-value="functionName"
                      label="Function Name"
                      placeholder="Select function"
                      variant="outlined"
                      @update:model-value="onFunctionChange('grandchild', $event, grandchildrenModal.parentChildIndex, grandIndex)"
                    />
                  </v-col>
                  <v-col cols="6">
                    <v-text-field
                      v-model="grandChild.description"
                      label="Short description"
                      variant="outlined"
                    />
                  </v-col>
                  <v-col cols="4">
                    <v-text-field
                      v-model.number="grandChild.retries"
                      label="Retries"
                      type="number"
                      min="0"
                      max="10"
                      variant="outlined"
                    />
                  </v-col>
                  <v-col cols="8">
                    <v-combobox
                      v-model="grandChild.retryIntervals"
                      :items="retryIntervalItems"
                      item-title="title"
                      item-value="value"
                      label="Retry intervals (seconds)"
                      placeholder="Select intervals or type custom values"
                      variant="outlined"
                      multiple
                      chips
                      closable-chips
                    />
                  </v-col>
                  <v-col cols="12">
                    <v-textarea
                      v-model="grandChild.requestData"
                      label="Request Data"
                      placeholder='{"key": "value"}'
                      variant="outlined"
                      rows="4"
                    />
                  </v-col>
                  <v-col cols="6" v-if="!grandChild.ignoreDateTime">
                    <v-date-input
                      v-model="grandChild.executionDate"
                      label="Execution Date"
                      variant="outlined"
                      :min="new Date().toISOString().split('T')[0]"
                      prepend-icon=""
                      prepend-inner-icon="$calendar"
                    />
                  </v-col>
                  <v-col cols="6" v-if="!grandChild.ignoreDateTime">
                    <v-text-field
                      v-model="grandChild.executionTime"
                      label="Enter Time (HH:mm:ss)"
                      placeholder="HH:mm:ss"
                      variant="outlined"
                      prepend-icon=""
                      prepend-inner-icon="mdi-clock-outline"
                    />
                  </v-col>
                  <v-col cols="12">
                    <v-checkbox-btn
                      v-model="grandChild.ignoreDateTime"
                      label="Execute immediately (with a one-second delay)"
                    />
                  </v-col>
                  <v-col cols="6">
                    <v-select
                      v-model="grandChild.runCondition"
                      :items="runConditions"
                      label="Run Condition"
                      placeholder="Select condition"
                      variant="outlined"
                    />
                  </v-col>
                </v-row>
              </v-container>
            </v-form>
          </v-window-item>
        </v-window>
      </v-card-text>

      <v-card-actions class="pa-4 flex-shrink-0">
        <v-spacer />
        <v-btn
          color="primary"
          variant="elevated"
          @click="grandchildrenModal.isOpen = false"
        >
          Done
        </v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { useFunctionNameStore } from '@/stores/functionNames'
import { tickerService } from '@/http/services/tickerService'
import { timeTickerService } from '@/http/services/timeTickerService'

// Props
interface Props {
  modelValue: boolean
  functionNames?: string[]
}

const props = withDefaults(defineProps<Props>(), {
  functionNames: () => []
})

// Emits
interface Emits {
  (e: 'update:modelValue', value: boolean): void
  (e: 'created', data: any): void
}

const emit = defineEmits<Emits>()

// Store
const functionNamesStore = useFunctionNameStore()
const getTickerRequestData = tickerService.getRequestData()
const addChainJobs = timeTickerService.addChainJobs()

// Reactive state
const isOpen = computed({
  get: () => props.modelValue,
  set: (value) => emit('update:modelValue', value)
})

const currentStep = ref('parent')
const activeChildIndex = ref(0)
const activeGrandChildIndexes = ref([0, 0, 0, 0, 0])
const isCreating = ref(false)

// Grandchildren Modal
const grandchildrenModal = ref({
  isOpen: false,
  parentChildIndex: 0
})

// Job data
const parentJob = ref({
  functionName: '',
  description: '',
  executionDate: undefined as Date | undefined,
  executionTime: '',
  ignoreDateTime: false,
  retries: 0,
  requestData: '',
  retryIntervals: [] as any[]
})

const children = ref([] as Array<{
  functionName: string
  description: string
  runCondition: string | null
  executionDate: Date | undefined
  executionTime: string
  ignoreDateTime: boolean
  retries: number
  requestData: string
  retryIntervals: any[]
  grandChildren: Array<{
    functionName: string
    description: string
    runCondition: string | null
    executionDate: Date | undefined
    executionTime: string
    ignoreDateTime: boolean
    retries: number
    requestData: string
    retryIntervals: any[]
  }>
}>)

// Computed properties
const functionNames = computed(() => {
  // Use function names from store if available (same as AddTimeTicker)
  return functionNamesStore.data || []
})

const runConditions = computed(() => [
  { title: 'On Success', value: 0 },
  { title: 'On Failure', value: 1 },
  { title: 'On Cancelled', value: 2 },
  { title: 'On Failure or Cancelled', value: 3 },
  { title: 'On Any Completed Status', value: 4 },
  { title: 'In Progress (Parallel)', value: 5 }
])

const retryIntervalItems = [
  { header: true, title: 'Select suggested intervals or create one' },
  { id: 1, title: '1 min', value: 60 },
  { id: 2, title: '5 min', value: 300 },
  { id: 3, title: '10 min', value: 600 },
  { id: 4, title: '30 min', value: 1800 },
  { id: 5, title: '1 hour', value: 3600 }
]

const steps = computed(() => [
  {
    id: 'parent',
    title: 'Parent Job',
    description: 'Configure the main job',
    icon: 'mdi-crown',
    completed: !!parentJob.value.functionName
  },
  {
    id: 'children',
    title: 'Children',
    description: 'Add child jobs and grandchildren',
    icon: 'mdi-account-group',
    completed: children.value.some(c => c.functionName)
  },
  {
    id: 'review',
    title: 'Review',
    description: 'Review and create',
    icon: 'mdi-check-circle',
    completed: false
  }
])

const completedStepsCount = computed(() => {
  return steps.value.filter(s => s.completed).length
})

const progressPercentage = computed(() => {
  return Math.round((completedStepsCount.value / steps.value.length) * 100)
})

// Example data computed properties
const currentExampleData = computed(() => {
  const currentFunction = getCurrentFunction()
  if (!currentFunction || !functionNamesStore.data) return null
  
  const functionData = functionNamesStore.data.find(fn => fn.functionName === currentFunction)
  return functionData?.functionRequestType || null
})

const currentRequestType = computed(() => {
  const currentFunction = getCurrentFunction()
  if (!currentFunction || !functionNamesStore.data) return 'Select a function to see the request type'
  
  const functionData = functionNamesStore.data.find(fn => fn.functionName === currentFunction)
  const requestType = functionData?.functionRequestNamespace
  return requestType === '' ? 'No request data' : requestType || 'Unknown request type'
})

const getCurrentFunction = () => {
  if (currentStep.value === 'parent') {
    return parentJob.value.functionName
  } else if (currentStep.value === 'children' && activeChildIndex.value >= 0) {
    return children.value[activeChildIndex.value]?.functionName
  } else if (currentStep.value === 'grandchildren' && grandchildrenModal.value.parentChildIndex >= 0) {
    const childIndex = grandchildrenModal.value.parentChildIndex
    const grandIndex = activeGrandChildIndexes.value[childIndex] || 0
    return children.value[childIndex]?.grandChildren[grandIndex]?.functionName
  }
  return null
}


// Methods
const closeModal = () => {
  isOpen.value = false
  resetForm()
}

const resetForm = () => {
  currentStep.value = 'parent'
  activeChildIndex.value = 0
  activeGrandChildIndexes.value = [0, 0, 0, 0, 0]
  
  parentJob.value = {
    functionName: '',
    description: '',
    executionDate: undefined,
    executionTime: '',
    ignoreDateTime: false,
    retries: 0,
    requestData: '',
    retryIntervals: []
  }
  
  children.value = []
}

const canNavigateToStep = (stepId: string) => {
  if (stepId === 'parent') return true
  if (stepId === 'children') return !!parentJob.value.functionName
  if (stepId === 'review') return !!parentJob.value.functionName
  return false
}

const navigateToStep = (stepId: string) => {
  if (canNavigateToStep(stepId)) {
    currentStep.value = stepId
  }
}

const getStepIconColor = (step: any) => {
  if (step.completed) return 'success'
  if (currentStep.value === step.id) return 'primary'
  return 'grey'
}


const getCurrentStepTitle = () => {
  const step = steps.value.find(s => s.id === currentStep.value)
  return step?.title || 'Unknown Step'
}

const canGoBack = () => {
  const stepOrder = ['parent', 'children', 'review']
  const currentIndex = stepOrder.indexOf(currentStep.value)
  return currentIndex > 0
}

const canGoNext = () => {
  const stepOrder = ['parent', 'children', 'review']
  const currentIndex = stepOrder.indexOf(currentStep.value)
  if (currentIndex === -1) return false
  if (currentIndex === stepOrder.length - 1) return false
  if (currentStep.value === 'parent') return !!parentJob.value.functionName
  return true
}

const goBack = () => {
  const stepOrder = ['parent', 'children', 'review']
  const currentIndex = stepOrder.indexOf(currentStep.value)
  if (currentIndex > 0) {
    currentStep.value = stepOrder[currentIndex - 1]
  }
}

const goNext = () => {
  const stepOrder = ['parent', 'children', 'review']
  const currentIndex = stepOrder.indexOf(currentStep.value)
  if (currentIndex < stepOrder.length - 1) {
    currentStep.value = stepOrder[currentIndex + 1]
  }
}


const onFunctionChange = (level: string, functionName: string, childIndex?: number, grandIndex?: number) => {
  // Get example data from function names store and set it as request data
  if (functionName && functionNamesStore.data) {
    const functionData = functionNamesStore.data.find(fn => fn.functionName === functionName)
    if (functionData?.functionRequestType) {
      let formattedData = ''
      try {
        formattedData = JSON.stringify(JSON.parse(functionData.functionRequestType), null, 2)
      } catch {
        formattedData = functionData.functionRequestType
      }
      
      // Set the request data for the appropriate level
      if (level === 'parent') {
        // Only set if request data is empty (don't overwrite existing data)
        if (!parentJob.value.requestData) {
          parentJob.value.requestData = formattedData
        }
      } else if (level === 'child' && childIndex !== undefined) {
        if (!children.value[childIndex]?.requestData) {
          children.value[childIndex].requestData = formattedData
        }
      } else if (level === 'grandchild' && childIndex !== undefined && grandIndex !== undefined) {
        if (!children.value[childIndex]?.grandChildren[grandIndex]?.requestData) {
          children.value[childIndex].grandChildren[grandIndex].requestData = formattedData
        }
      }
    }
  }
}

// Format JSON for display (similar to AddTimeTicker)
const formatJsonForDisplay = (json: string | null, isHtml: boolean = false) => {
  if (!json) return ''
  try {
    const formatted = JSON.stringify(JSON.parse(json), null, 2)
    return isHtml ? formatted.replace(/\n/g, '<br>').replace(/ /g, '&nbsp;') : formatted
  } catch (error) {
    return isHtml ? json.replace(/\n/g, '<br>').replace(/ /g, '&nbsp;') : json
  }
}

// Copy example data to current request data field
const copyExampleToRequestData = () => {
  if (!currentExampleData.value) return
  
  const formattedData = formatJsonForDisplay(currentExampleData.value, false)
  
  if (currentStep.value === 'parent') {
    parentJob.value.requestData = formattedData
  } else if (currentStep.value === 'children' && activeChildIndex.value >= 0) {
    children.value[activeChildIndex.value].requestData = formattedData
  } else if (currentStep.value === 'grandchildren' && grandchildrenModal.value.parentChildIndex >= 0) {
    const childIndex = grandchildrenModal.value.parentChildIndex
    const grandIndex = activeGrandChildIndexes.value[childIndex] || 0
    if (children.value[childIndex]?.grandChildren[grandIndex]) {
      children.value[childIndex].grandChildren[grandIndex].requestData = formattedData
    }
  }
}


const openGrandchildrenModal = (childIndex: number) => {
  grandchildrenModal.value.parentChildIndex = childIndex
  grandchildrenModal.value.isOpen = true
}

// Helper functions to create new child/grandchild objects
const createNewChild = () => ({
  functionName: '',
  description: '',
  runCondition: null,
  executionDate: undefined as Date | undefined,
  executionTime: '',
  ignoreDateTime: false,
  retries: 0,
  requestData: '',
  retryIntervals: [] as any[],
  grandChildren: [] as Array<{
    functionName: string
    description: string
    runCondition: string | null
    executionDate: Date | undefined
    executionTime: string
    ignoreDateTime: boolean
    retries: number
    requestData: string
    retryIntervals: any[]
  }>
})

const createNewGrandChild = () => ({
  functionName: '',
  description: '',
  runCondition: null,
  executionDate: undefined as Date | undefined,
  executionTime: '',
  ignoreDateTime: false,
  retries: 0,
  requestData: '',
  retryIntervals: [] as any[]
})

// Add/Remove children methods
const addChild = () => {
  if (children.value.length < 5) {
    children.value.push(createNewChild())
    activeChildIndex.value = children.value.length - 1
  }
}

const removeChild = (index: number) => {
  if (children.value.length > 0) {
    children.value.splice(index, 1)
    if (activeChildIndex.value >= children.value.length) {
      activeChildIndex.value = Math.max(0, children.value.length - 1)
    }
  }
}

// Add/Remove grandchildren methods
const addGrandChild = (childIndex: number) => {
  if (children.value[childIndex].grandChildren.length < 5) {
    children.value[childIndex].grandChildren.push(createNewGrandChild())
    activeGrandChildIndexes.value[childIndex] = children.value[childIndex].grandChildren.length - 1
  }
}

const removeGrandChild = (childIndex: number, grandIndex: number) => {
  if (children.value[childIndex].grandChildren.length > 0) {
    children.value[childIndex].grandChildren.splice(grandIndex, 1)
    if (activeGrandChildIndexes.value[childIndex] >= children.value[childIndex].grandChildren.length) {
      activeGrandChildIndexes.value[childIndex] = Math.max(0, children.value[childIndex].grandChildren.length - 1)
    }
  }
}

const getGrandchildrenCount = (childIndex: number) => {
  return children.value[childIndex].grandChildren.filter(gc => gc.functionName).length
}

const getConfiguredChildrenCount = () => {
  return children.value.filter(c => c.functionName).length
}

const getConfiguredChildren = () => {
  return children.value
    .map((child, index) => ({
      ...child,
      index,
      grandChildrenCount: getGrandchildrenCount(index)
    }))
    .filter(child => child.functionName)
}

const formatExecutionTime = (job: any) => {
  if (job.ignoreDateTime) return 'Immediate'
  if (job.executionDate && job.executionTime) {
    return `${job.executionDate.toLocaleDateString()} at ${job.executionTime}`
  }
  return 'Not set'
}

const createChainJobs = async () => {
  isCreating.value = true
  
  try {
    const getExecutionTime = (job: any) => {
      if (job.ignoreDateTime) return undefined
      if (job.executionDate && job.executionTime) {
        const [hours, minutes, seconds = 0] = job.executionTime.split(':').map(Number)
        const parsedExecutionDate = new Date(job.executionDate).setHours(hours, minutes, seconds, 0)
        return new Date(parsedExecutionDate).toISOString()
      }
      return null
    }

    const chainRoot = {
      function: parentJob.value.functionName,
      description: parentJob.value.description,
      executionTime: getExecutionTime(parentJob.value),
      retries: parentJob.value.retries,
      request: parentJob.value.requestData || null,
      intervals: parentJob.value.retryIntervals?.map(item => typeof item === 'object' ? item.value : item) || [],
      children: [] as any[]
    }

    children.value.forEach((child) => {
      if (child.functionName) {
        const childEntity = {
          function: child.functionName,
          description: child.description,
          runCondition: child.runCondition,
          executionTime: getExecutionTime(child),
          retries: child.retries,
          request: child.requestData || null,
          intervals: child.retryIntervals?.map(item => typeof item === 'object' ? item.value : item) || [],
          children: [] as any[]
        }

        child.grandChildren.forEach((grandChild) => {
          if (grandChild.functionName) {
            childEntity.children.push({
              function: grandChild.functionName,
              description: grandChild.description,
              runCondition: grandChild.runCondition,
              executionTime: getExecutionTime(grandChild),
              retries: grandChild.retries,
              request: grandChild.requestData || null,
              intervals: grandChild.retryIntervals?.map(item => typeof item === 'object' ? item.value : item) || [],
              children: []
            })
          }
        })
        chainRoot.children.push(childEntity)
      }
    })

    const addChainJobs = timeTickerService.addChainJobs()
    addChainJobs.requestAsync(chainRoot)
        .then((result) => {
            emit('created', result)
            closeModal()
        })
        .catch((error) => {
            console.error('Failed to create chain jobs:', error)
        })
        .finally(() => {
           isCreating.value = false
        })
  } catch (error) {
    console.error('Error creating chain jobs:', error)
    isCreating.value = false
  }
}

// Lifecycle
onMounted(async () => {
  // Load function names from store
  await functionNamesStore.loadData();
});
</script>

<style scoped>
/* Main Modal Styles */
.chain-modal {
  height: 85vh;
  max-height: 85vh;
  display: flex;
  flex-direction: column;
}

/* Header Styles */
.chain-header {
  background: linear-gradient(135deg, rgba(25, 118, 210, 0.1) 0%, rgba(25, 118, 210, 0.05) 100%);
  border-bottom: 1px solid rgba(25, 118, 210, 0.2);
  padding: 12px 20px;
  flex-shrink: 0;
}

.header-content {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
}

.header-left {
  display: flex;
  align-items: center;
  gap: 16px;
}

.header-icon {
  font-size: 32px;
  color: #1976d2;
}

.header-title {
  font-size: 20px;
  font-weight: 700;
  color: #ffffff;
  margin: 0;
}

.header-divider {
  border-color: rgba(25, 118, 210, 0.2);
  flex-shrink: 0;
}

/* Content Layout */
.chain-content {
  flex: 1;
  overflow: hidden;
  height: calc(85vh - 120px); /* Account for header and padding */
  min-height: 0;
}

.content-row {
  height: 100%;
  min-height: 0;
}

/* Sidebar Styles */
.sidebar {
  background: rgba(0, 0, 0, 0.1);
  border-right: 1px solid rgba(255, 255, 255, 0.1);
  height: 100%;
  min-height: 0;
}

.sidebar-inner {
  height: 100%;
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 16px;
  overflow-y: auto;
  min-height: 0;
}

/* Progress Tracker */
.progress-tracker {
  background: rgba(0, 0, 0, 0.2);
  border-radius: 8px;
  padding: 16px;
  border: 1px solid rgba(255, 255, 255, 0.1);
}

.tracker-title {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 16px;
  font-weight: 600;
  color: #ffffff;
  margin: 0 0 12px 0;
}

.tracker-icon {
  color: #1976d2;
}

/* Progress Bar Section */
.progress-bar-section {
  margin-bottom: 16px;
}

.progress-bar {
  margin-bottom: 0;
}

.steps-container {
  position: relative;
  margin-bottom: 20px;
}

.step-item {
  display: flex;
  align-items: center;
  padding: 8px;
  border-radius: 6px;
  cursor: pointer;
  transition: all 0.2s ease;
  position: relative;
  margin-bottom: 4px;
}

.step-item:hover.step-clickable {
  background: rgba(25, 118, 210, 0.1);
  transform: translateX(4px);
}

.step-active {
  background: rgba(25, 118, 210, 0.15);
  border-left: 3px solid #1976d2;
}

.step-completed {
  background: rgba(76, 175, 80, 0.1);
  border-left: 2px solid #4caf50;
}

.step-item:not(.step-clickable) {
  cursor: not-allowed;
  opacity: 0.5;
}

.step-indicator {
  width: 24px;
  height: 24px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  margin-right: 8px;
  flex-shrink: 0;
  background: rgba(255, 255, 255, 0.1);
  border: 1px solid rgba(255, 255, 255, 0.2);
}

.step-number {
  font-size: 11px;
  font-weight: 600;
  color: #ffffff;
}

.step-content {
  flex: 1;
  min-width: 0;
}

.step-title {
  font-weight: 500;
  font-size: 13px;
  color: #ffffff;
}

.step-connector {
  position: absolute;
  left: 20px;
  top: 32px;
  width: 2px;
  height: 12px;
  background: rgba(255, 255, 255, 0.2);
}

.step-item:last-child .step-connector {
  display: none;
}

/* Progress Bar */
.progress-bar-container {
  display: flex;
  align-items: center;
  gap: 12px;
}

.progress-bar {
  flex: 1;
}

.progress-text {
  font-size: 12px;
  color: #b0bec5;
  min-width: 80px;
  text-align: right;
}


/* Form Area */
.form-area {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 0;
}

.form-inner {
  height: 100%;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

/* Step Navigation */
.step-nav {
  background: rgba(0, 0, 0, 0.1);
  border-bottom: 1px solid rgba(255, 255, 255, 0.1);
  padding: 12px 20px;
  flex-shrink: 0;
}

.nav-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.nav-title {
  font-size: 20px;
  font-weight: 600;
  color: #ffffff;
  margin: 0;
}

.nav-controls {
  display: flex;
  gap: 12px;
  align-items: center;
}

/* Bottom Navigation */
.bottom-nav {
  display: flex;
  align-items: center;
  padding: 16px 20px;
  border-top: 1px solid rgba(255, 255, 255, 0.1);
  background: rgba(0, 0, 0, 0.1);
  flex-shrink: 0;
}

/* Form Content */
.form-content {
  flex: 1;
  overflow: hidden;
  min-height: 0;
}

.step-window {
  height: 100%;
}

.step-content {
  height: 100%;
  padding: 16px 20px;
  overflow-y: auto;
  min-height: 0;
}

.step-alert {
  margin-bottom: 16px;
}

/* Form Styling */
.step-content h4 {
  color: #e0e0e0;
  font-weight: 600;
  font-size: 16px;
}

.step-content h5 {
  color: #b0bec5;
  font-weight: 500;
  font-size: 14px;
}

/* Headers for Add/Remove sections */
.children-header, .grandchildren-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 16px;
  background: rgba(0, 0, 0, 0.1);
  border-radius: 8px;
  margin-bottom: 16px;
}

.children-title h4, .grandchildren-title h5 {
  margin: 0;
  color: #e0e0e0;
}

.no-children-message, .no-grandchildren-message {
  margin: 20px 0;
}

/* Children Specific Styles */
.children-container {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.children-tabs {
  background: rgba(0, 0, 0, 0.1);
  border-radius: 8px;
  padding: 4px;
}

.child-tab {
  position: relative;
}

.tab-icon {
  margin-right: 6px;
}

.tab-indicator {
  margin-left: 6px;
}

.children-window {
  margin-top: 16px;
}

.child-content {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.child-details {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

/* Grandchildren Modal */
.grandchildren-modal {
  height: 70vh;
  max-height: 70vh;
  display: flex;
  flex-direction: column;
}

.grandchildren-content {
  flex: 1;
  overflow-y: auto;
  min-height: 0;
  padding: 0 !important;
}

.grandchildren-tabs {
  background: rgba(156, 39, 176, 0.1);
  border-bottom: 1px solid rgba(255, 255, 255, 0.1);
  flex-shrink: 0;
}

.grandchildren-window {
  flex: 1;
  overflow-y: auto;
  min-height: 0;
}

.grandchild-content {
  padding: 20px;
  overflow-y: auto;
}

/* Review Styles */
.review-content {
  display: flex;
  flex-direction: column;
  gap: 24px;
}

.review-section h4 {
  color: #e0e0e0;
  font-weight: 600;
  font-size: 16px;
}

.review-details {
  padding: 16px;
  background: rgba(0, 0, 0, 0.1);
  border-radius: 8px;
  border: 1px solid rgba(255, 255, 255, 0.1);
}

.review-item {
  margin-bottom: 8px;
  padding: 4px 0;
}

.child-summary {
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  padding: 16px;
  margin-bottom: 12px;
  background: rgba(0, 0, 0, 0.1);
}

.child-header {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 8px;
}

.child-icon {
  color: #1976d2;
}

.child-details {
  margin-left: 32px;
  font-size: 14px;
  color: #b0bec5;
}

.child-details > div {
  margin-bottom: 4px;
}

.empty-state {
  text-align: center;
  color: #9e9e9e;
  font-style: italic;
  padding: 20px;
}

/* Example Data Panel */
.example-data-panel {
  background: rgba(0, 0, 0, 0.2);
  border-radius: 8px;
  padding: 16px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  margin-top: 16px;
}

.example-card {
  background: rgba(0, 0, 0, 0.3) !important;
  border: 1px solid rgba(255, 255, 255, 0.1);
}

.example-card-title {
  font-size: 14px !important;
  font-weight: 600;
  color: #ffffff;
  padding: 0 !important;
}

.example-card-subtitle {
  font-size: 12px !important;
  color: rgba(255, 255, 255, 0.7);
  padding: 0 !important;
}

.example-content {
  font-family: 'Monaco', 'Menlo', 'Ubuntu Mono', monospace;
  font-size: 11px;
  background: rgba(0, 0, 0, 0.4);
  border-radius: 4px;
  max-height: 200px;
  overflow-y: auto;
  line-height: 1.4;
  color: #ffffff;
}

.no-example-text {
  color: rgba(255, 255, 255, 0.6);
  font-style: italic;
  text-align: center;
  padding: 16px;
}

/* Responsive adjustments */
@media (max-width: 960px) {
  .sidebar {
    display: none;
  }
  
  .form-area {
    flex: 1;
  }
  
  .header-title {
    font-size: 20px;
  }
  
  .header-subtitle {
    font-size: 12px;
  }
}
</style>
