// Components
import { VTooltip } from "../../components/VTooltip/index.mjs"; // Composables
import { useDirectiveComponent } from "../../composables/directiveComponent.mjs"; // Types
export const Tooltip = useDirectiveComponent(VTooltip, binding => {
  return {
    activator: 'parent',
    location: binding.arg?.replace('-', ' '),
    text: typeof binding.value === 'boolean' ? undefined : binding.value
  };
});
export default Tooltip;
//# sourceMappingURL=index.mjs.map