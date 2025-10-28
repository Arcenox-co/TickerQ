import { ref, computed, type Ref } from 'vue'

export interface PaginationOptions {
  initialPage?: number
  initialPageSize?: number
  pageSizeOptions?: number[]
}

export interface PaginationState {
  currentPage: Ref<number>
  pageSize: Ref<number>
  totalCount: Ref<number>
  totalPages: Ref<number>
  hasNextPage: Ref<boolean>
  hasPreviousPage: Ref<boolean>
  firstItemIndex: Ref<number>
  lastItemIndex: Ref<number>
  pageSizeOptions: number[]
  handlePageChange: (page: number) => Promise<void>
  handlePageSizeChange: (size: number) => Promise<void>
  reset: () => void
}

export function usePagination(
  loadDataFn: (page: number, pageSize: number) => Promise<{ totalCount?: number } | void>,
  options: PaginationOptions = {}
): PaginationState {
  const {
    initialPage = 1,
    initialPageSize = 20,
    pageSizeOptions = [10, 20, 50, 100]
  } = options

  // Reactive state
  const currentPage = ref(initialPage)
  const pageSize = ref(initialPageSize)
  const totalCount = ref(0)
  const isLoading = ref(false)

  // Computed properties
  const totalPages = computed(() => 
    Math.ceil(totalCount.value / pageSize.value) || 1
  )

  const hasNextPage = computed(() => 
    currentPage.value < totalPages.value
  )

  const hasPreviousPage = computed(() => 
    currentPage.value > 1
  )

  const firstItemIndex = computed(() => 
    totalCount.value > 0 ? (currentPage.value - 1) * pageSize.value + 1 : 0
  )

  const lastItemIndex = computed(() => 
    Math.min(currentPage.value * pageSize.value, totalCount.value)
  )

  // Load page data with error handling
  const loadPageData = async () => {
    if (isLoading.value) return
    
    try {
      isLoading.value = true
      const response = await loadDataFn(currentPage.value, pageSize.value)
      
      if (response && typeof response.totalCount === 'number') {
        totalCount.value = response.totalCount
      }
      
      // Validate current page is within bounds
      if (currentPage.value > totalPages.value && totalPages.value > 0) {
        currentPage.value = totalPages.value
        await loadDataFn(currentPage.value, pageSize.value)
      }
    } catch (error) {
      console.error('Failed to load paginated data:', error)
      // Could emit an error event or show a notification here
    } finally {
      isLoading.value = false
    }
  }

  // Handle page change
  const handlePageChange = async (page: number) => {
    if (page < 1 || page > totalPages.value) return
    if (page === currentPage.value) return
    
    currentPage.value = page
    await loadPageData()
  }

  // Handle page size change
  const handlePageSizeChange = async (size: number) => {
    if (size === pageSize.value) return
    
    pageSize.value = size
    currentPage.value = 1 // Reset to first page
    await loadPageData()
  }

  // Reset pagination to initial state
  const reset = () => {
    currentPage.value = initialPage
    pageSize.value = initialPageSize
    totalCount.value = 0
  }

  return {
    currentPage,
    pageSize,
    totalCount,
    totalPages,
    hasNextPage,
    hasPreviousPage,
    firstItemIndex,
    lastItemIndex,
    pageSizeOptions,
    handlePageChange,
    handlePageSizeChange,
    reset
  }
}
