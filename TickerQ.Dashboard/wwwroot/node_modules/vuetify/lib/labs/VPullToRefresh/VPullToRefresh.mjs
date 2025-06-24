import { createVNode as _createVNode } from "vue";
// Styles
import "./VPullToRefresh.css";

// Components
import { VIcon } from "../../components/VIcon/index.mjs";
import { VProgressCircular } from "../../components/VProgressCircular/index.mjs"; // Utilities
import { computed, onMounted, ref, shallowRef, watch } from 'vue';
import { clamp, convertToUnit, genericComponent, getScrollParents, useRender } from "../../util/index.mjs";
export const VPullToRefresh = genericComponent()({
  name: 'VPullToRefresh',
  props: {
    disabled: Boolean,
    pullDownThreshold: {
      type: Number,
      default: 64
    }
  },
  emits: {
    load: options => true
  },
  setup(props, _ref) {
    let {
      slots,
      emit
    } = _ref;
    let touchstartY = 0;
    let scrollParents = [];
    const touchDiff = shallowRef(0);
    const containerRef = ref();
    const refreshing = shallowRef(false);
    const goingUp = shallowRef(false);
    const touching = shallowRef(false);
    const canRefresh = computed(() => touchDiff.value >= props.pullDownThreshold && !refreshing.value);
    const topOffset = computed(() => clamp(touchDiff.value, 0, props.pullDownThreshold));
    function onTouchstart(e) {
      if (refreshing.value || props.disabled) return;
      touching.value = true;
      touchstartY = 'clientY' in e ? e.clientY : e.touches[0].clientY;
    }
    function onTouchmove(e) {
      if (refreshing.value || !touching.value || props.disabled) return;
      const touchY = 'clientY' in e ? e.clientY : e.touches[0].clientY;
      if (scrollParents.length && !scrollParents[0].scrollTop) {
        touchDiff.value = touchY - touchstartY;
      }
    }
    function onTouchend(e) {
      if (refreshing.value || props.disabled) return;
      touching.value = false;
      if (canRefresh.value) {
        function done() {
          if (!refreshing.value) return;
          touchDiff.value = 0;
          refreshing.value = false;
        }
        emit('load', {
          done
        });
        refreshing.value = true;
      } else {
        touchDiff.value = 0;
      }
    }
    onMounted(() => {
      scrollParents = getScrollParents(containerRef.value);
    });
    watch([topOffset, refreshing], () => {
      if (scrollParents.length) {
        const stopScrolling = topOffset.value && !refreshing.value;
        scrollParents.forEach(p => p.style.overflow = stopScrolling ? 'hidden' : 'auto');
      }
    });
    watch(topOffset, (newVal, oldVal) => {
      goingUp.value = newVal < oldVal;
    });
    useRender(() => {
      return _createVNode("div", {
        "class": ['v-pull-to-refresh'],
        "onTouchstart": onTouchstart,
        "onTouchmove": onTouchmove,
        "onTouchend": onTouchend,
        "onMousedown": onTouchstart,
        "onMouseup": onTouchend,
        "onMouseleave": onTouchend,
        "onMousemove": onTouchmove,
        "ref": containerRef
      }, [_createVNode("div", {
        "class": ['v-pull-to-refresh__pull-down', {
          'v-pull-to-refresh__pull-down--touching': touching.value
        }],
        "style": {
          top: convertToUnit(-1 * props.pullDownThreshold + topOffset.value),
          height: convertToUnit(props.pullDownThreshold)
        }
      }, [slots.pullDownPanel ? slots.pullDownPanel({
        canRefresh: canRefresh.value,
        goingUp: goingUp.value,
        refreshing: refreshing.value
      }) : _createVNode("div", {
        "class": ['v-pull-to-refresh__pull-down-default']
      }, [refreshing.value ? _createVNode(VProgressCircular, {
        "indeterminate": true,
        "active": false
      }, null) : _createVNode(VIcon, {
        "icon": canRefresh.value || goingUp.value ? '$sortAsc' : '$sortDesc'
      }, null)])]), _createVNode("div", {
        "class": ['v-pull-to-refresh__scroll-container', {
          'v-pull-to-refresh__scroll-container--touching': touching.value
        }],
        "style": {
          top: convertToUnit(topOffset.value)
        }
      }, [slots.default?.()])]);
    });
  }
});
//# sourceMappingURL=VPullToRefresh.mjs.map