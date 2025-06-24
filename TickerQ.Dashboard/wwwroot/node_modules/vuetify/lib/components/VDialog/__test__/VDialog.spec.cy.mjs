import { createVNode as _createVNode, createTextVNode as _createTextVNode } from "vue";
/// <reference types="../../../../types/cypress" />
// Components
import { VDialog } from "../index.mjs"; // Utilities
import { ref } from 'vue';

// Tests
describe('VDialog', () => {
  it('should render correctly', () => {
    const model = ref(false);
    cy.mount(() => _createVNode(VDialog, {
      "modelValue": model.value,
      "onUpdate:modelValue": $event => model.value = $event,
      "data-test": "dialog"
    }, {
      default: () => [_createVNode("div", {
        "data-test": "content"
      }, [_createTextVNode("Content")])]
    })).get('[data-test="dialog"]').should('not.exist').then(() => {
      model.value = true;
    }).get('[data-test="dialog"]').should('be.visible').get('[data-test="content"]').should('be.visible').get('body').click().then(() => {
      expect(model.value).to.be.false;
    }).get('[data-test="dialog"]').should('not.exist').get('[data-test="content"]').should('not.exist');
  });
  it('should emit afterLeave', () => {
    const model = ref(true);
    const onAfterLeave = cy.spy().as('afterLeave');
    cy.mount(() => _createVNode(VDialog, {
      "modelValue": model.value,
      "onUpdate:modelValue": $event => model.value = $event,
      "onAfterLeave": onAfterLeave
    }, {
      default: () => [_createVNode("div", {
        "data-test": "content"
      }, [_createTextVNode("Content")])]
    })).get('body').click().get('@afterLeave').should('have.been.calledOnce');
  });
});
//# sourceMappingURL=VDialog.spec.cy.mjs.map