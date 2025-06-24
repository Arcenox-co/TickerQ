import { createVNode as _createVNode, mergeProps as _mergeProps } from "vue";
// Composables
import { useTextColor } from "../../composables/color.mjs";
import { makeComponentProps } from "../../composables/component.mjs";
import { makeRouterProps, useLink } from "../../composables/router.mjs";
import { makeTagProps } from "../../composables/tag.mjs"; // Utilities
import { computed } from 'vue';
import { genericComponent, propsFactory, useRender } from "../../util/index.mjs";
export const makeVBreadcrumbsItemProps = propsFactory({
  active: Boolean,
  activeClass: String,
  activeColor: String,
  color: String,
  disabled: Boolean,
  title: String,
  ...makeComponentProps(),
  ...makeRouterProps(),
  ...makeTagProps({
    tag: 'li'
  })
}, 'VBreadcrumbsItem');
export const VBreadcrumbsItem = genericComponent()({
  name: 'VBreadcrumbsItem',
  props: makeVBreadcrumbsItemProps(),
  setup(props, _ref) {
    let {
      slots,
      attrs
    } = _ref;
    const link = useLink(props, attrs);
    const isActive = computed(() => props.active || link.isActive?.value);
    const color = computed(() => isActive.value ? props.activeColor : props.color);
    const {
      textColorClasses,
      textColorStyles
    } = useTextColor(color);
    useRender(() => {
      return _createVNode(props.tag, {
        "class": ['v-breadcrumbs-item', {
          'v-breadcrumbs-item--active': isActive.value,
          'v-breadcrumbs-item--disabled': props.disabled,
          [`${props.activeClass}`]: isActive.value && props.activeClass
        }, textColorClasses.value, props.class],
        "style": [textColorStyles.value, props.style],
        "aria-current": isActive.value ? 'page' : undefined
      }, {
        default: () => [!link.isLink.value ? slots.default?.() ?? props.title : _createVNode("a", _mergeProps({
          "class": "v-breadcrumbs-item--link",
          "onClick": link.navigate
        }, link.linkProps), [slots.default?.() ?? props.title])]
      });
    });
    return {};
  }
});
//# sourceMappingURL=VBreadcrumbsItem.mjs.map