// Composables
import { useHydration } from "../../composables/hydration.mjs"; // Utilities
import { defineComponent } from "../../util/index.mjs";
export const VNoSsr = defineComponent({
  name: 'VNoSsr',
  setup(_, _ref) {
    let {
      slots
    } = _ref;
    const show = useHydration();
    return () => show.value && slots.default?.();
  }
});
//# sourceMappingURL=VNoSsr.mjs.map