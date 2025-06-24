import { createVNode as _createVNode, createTextVNode as _createTextVNode } from "vue";
// Styles
import "./VInfiniteScroll.css";

// Components
import { VBtn } from "../VBtn/index.mjs";
import { VProgressCircular } from "../VProgressCircular/index.mjs"; // Composables
import { makeDimensionProps, useDimension } from "../../composables/dimensions.mjs";
import { useIntersectionObserver } from "../../composables/intersectionObserver.mjs";
import { useLocale } from "../../composables/locale.mjs";
import { makeTagProps } from "../../composables/tag.mjs"; // Utilities
import { computed, nextTick, onMounted, ref, shallowRef, watch } from 'vue';
import { convertToUnit, defineComponent, genericComponent, propsFactory, useRender } from "../../util/index.mjs"; // Types
export const makeVInfiniteScrollProps = propsFactory({
  color: String,
  direction: {
    type: String,
    default: 'vertical',
    validator: v => ['vertical', 'horizontal'].includes(v)
  },
  side: {
    type: String,
    default: 'end',
    validator: v => ['start', 'end', 'both'].includes(v)
  },
  mode: {
    type: String,
    default: 'intersect',
    validator: v => ['intersect', 'manual'].includes(v)
  },
  margin: [Number, String],
  loadMoreText: {
    type: String,
    default: '$vuetify.infiniteScroll.loadMore'
  },
  emptyText: {
    type: String,
    default: '$vuetify.infiniteScroll.empty'
  },
  ...makeDimensionProps(),
  ...makeTagProps()
}, 'VInfiniteScroll');
export const VInfiniteScrollIntersect = defineComponent({
  name: 'VInfiniteScrollIntersect',
  props: {
    side: {
      type: String,
      required: true
    },
    rootMargin: String
  },
  emits: {
    intersect: (side, isIntersecting) => true
  },
  setup(props, _ref) {
    let {
      emit
    } = _ref;
    const {
      intersectionRef,
      isIntersecting
    } = useIntersectionObserver();
    watch(isIntersecting, async val => {
      emit('intersect', props.side, val);
    });
    useRender(() => _createVNode("div", {
      "class": "v-infinite-scroll-intersect",
      "style": {
        '--v-infinite-margin-size': props.rootMargin
      },
      "ref": intersectionRef
    }, [_createTextVNode("\xA0")]));
    return {};
  }
});
export const VInfiniteScroll = genericComponent()({
  name: 'VInfiniteScroll',
  props: makeVInfiniteScrollProps(),
  emits: {
    load: options => true
  },
  setup(props, _ref2) {
    let {
      slots,
      emit
    } = _ref2;
    const rootEl = ref();
    const startStatus = shallowRef('ok');
    const endStatus = shallowRef('ok');
    const margin = computed(() => convertToUnit(props.margin));
    const isIntersecting = shallowRef(false);
    function setScrollAmount(amount) {
      if (!rootEl.value) return;
      const property = props.direction === 'vertical' ? 'scrollTop' : 'scrollLeft';
      rootEl.value[property] = amount;
    }
    function getScrollAmount() {
      if (!rootEl.value) return 0;
      const property = props.direction === 'vertical' ? 'scrollTop' : 'scrollLeft';
      return rootEl.value[property];
    }
    function getScrollSize() {
      if (!rootEl.value) return 0;
      const property = props.direction === 'vertical' ? 'scrollHeight' : 'scrollWidth';
      return rootEl.value[property];
    }
    function getContainerSize() {
      if (!rootEl.value) return 0;
      const property = props.direction === 'vertical' ? 'clientHeight' : 'clientWidth';
      return rootEl.value[property];
    }
    onMounted(() => {
      if (!rootEl.value) return;
      if (props.side === 'start') {
        setScrollAmount(getScrollSize());
      } else if (props.side === 'both') {
        setScrollAmount(getScrollSize() / 2 - getContainerSize() / 2);
      }
    });
    function setStatus(side, status) {
      if (side === 'start') {
        startStatus.value = status;
      } else if (side === 'end') {
        endStatus.value = status;
      }
    }
    function getStatus(side) {
      return side === 'start' ? startStatus.value : endStatus.value;
    }
    let previousScrollSize = 0;
    function handleIntersect(side, _isIntersecting) {
      isIntersecting.value = _isIntersecting;
      if (isIntersecting.value) {
        intersecting(side);
      }
    }
    function intersecting(side) {
      if (props.mode !== 'manual' && !isIntersecting.value) return;
      const status = getStatus(side);
      if (!rootEl.value || ['empty', 'loading'].includes(status)) return;
      previousScrollSize = getScrollSize();
      setStatus(side, 'loading');
      function done(status) {
        setStatus(side, status);
        nextTick(() => {
          if (status === 'empty' || status === 'error') return;
          if (status === 'ok' && side === 'start') {
            setScrollAmount(getScrollSize() - previousScrollSize + getScrollAmount());
          }
          if (props.mode !== 'manual') {
            nextTick(() => {
              window.requestAnimationFrame(() => {
                window.requestAnimationFrame(() => {
                  window.requestAnimationFrame(() => {
                    intersecting(side);
                  });
                });
              });
            });
          }
        });
      }
      emit('load', {
        side,
        done
      });
    }
    const {
      t
    } = useLocale();
    function renderSide(side, status) {
      if (props.side !== side && props.side !== 'both') return;
      const onClick = () => intersecting(side);
      const slotProps = {
        side,
        props: {
          onClick,
          color: props.color
        }
      };
      if (status === 'error') return slots.error?.(slotProps);
      if (status === 'empty') return slots.empty?.(slotProps) ?? _createVNode("div", null, [t(props.emptyText)]);
      if (props.mode === 'manual') {
        if (status === 'loading') {
          return slots.loading?.(slotProps) ?? _createVNode(VProgressCircular, {
            "indeterminate": true,
            "color": props.color
          }, null);
        }
        return slots['load-more']?.(slotProps) ?? _createVNode(VBtn, {
          "variant": "outlined",
          "color": props.color,
          "onClick": onClick
        }, {
          default: () => [t(props.loadMoreText)]
        });
      }
      return slots.loading?.(slotProps) ?? _createVNode(VProgressCircular, {
        "indeterminate": true,
        "color": props.color
      }, null);
    }
    const {
      dimensionStyles
    } = useDimension(props);
    useRender(() => {
      const Tag = props.tag;
      const hasStartIntersect = props.side === 'start' || props.side === 'both';
      const hasEndIntersect = props.side === 'end' || props.side === 'both';
      const intersectMode = props.mode === 'intersect';
      return _createVNode(Tag, {
        "ref": rootEl,
        "class": ['v-infinite-scroll', `v-infinite-scroll--${props.direction}`, {
          'v-infinite-scroll--start': hasStartIntersect,
          'v-infinite-scroll--end': hasEndIntersect
        }],
        "style": dimensionStyles.value
      }, {
        default: () => [_createVNode("div", {
          "class": "v-infinite-scroll__side"
        }, [renderSide('start', startStatus.value)]), hasStartIntersect && intersectMode && _createVNode(VInfiniteScrollIntersect, {
          "key": "start",
          "side": "start",
          "onIntersect": handleIntersect,
          "rootMargin": margin.value
        }, null), slots.default?.(), hasEndIntersect && intersectMode && _createVNode(VInfiniteScrollIntersect, {
          "key": "end",
          "side": "end",
          "onIntersect": handleIntersect,
          "rootMargin": margin.value
        }, null), _createVNode("div", {
          "class": "v-infinite-scroll__side"
        }, [renderSide('end', endStatus.value)])]
      });
    });
  }
});
//# sourceMappingURL=VInfiniteScroll.mjs.map