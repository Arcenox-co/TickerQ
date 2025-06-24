// Utilities
import { h, resolveComponent } from 'vue';
import { aliases as faAliases } from "./fa.mjs"; // Types
const aliases = faAliases;
const fa = {
  component: props => {
    const {
      icon,
      tag,
      ...rest
    } = props;
    const stringIcon = icon;
    return h(tag, rest, [h(resolveComponent('font-awesome-icon'), {
      key: stringIcon,
      // TODO: https://github.com/FortAwesome/vue-fontawesome/issues/250
      icon: stringIcon.includes(' fa-') ? stringIcon.split(' fa-') : stringIcon
    })]);
  }
};
export { aliases, fa };
//# sourceMappingURL=fa-svg.mjs.map