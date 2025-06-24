import { createTextVNode as _createTextVNode, Fragment as _Fragment, createVNode as _createVNode } from "vue";
// Components
import { VConfirmEdit } from "../index.mjs"; // Utilities
import { render, screen, userEvent } from '@test';
import { nextTick, shallowRef } from 'vue';
describe('VConfirmEdit', () => {
  it('mirrors external updates', async () => {
    const externalModel = shallowRef('foo');
    render(() => _createVNode(VConfirmEdit, {
      "modelValue": externalModel.value
    }, {
      default: _ref => {
        let {
          model
        } = _ref;
        return _createVNode("p", null, [model.value]);
      }
    }));
    expect(screen.getByText('foo')).toBeInTheDocument();
    externalModel.value = 'bar';
    await nextTick();
    expect(screen.getByText('bar')).toBeInTheDocument();
  });
  it("doesn't mutate the original value", async () => {
    const externalModel = shallowRef(['foo']);
    render(() => _createVNode(VConfirmEdit, {
      "modelValue": externalModel.value,
      "onUpdate:modelValue": $event => externalModel.value = $event
    }, {
      default: _ref2 => {
        let {
          model
        } = _ref2;
        return _createVNode(_Fragment, null, [_createVNode("p", null, [model.value.join(',')]), _createVNode("button", {
          "data-testid": "push",
          "onClick": () => model.value.push('bar')
        }, [_createTextVNode("Push")])]);
      }
    }));
    expect(screen.getByText('foo')).toBeInTheDocument();
    await userEvent.click(screen.getByTestId('push'));
    expect(screen.getByText('foo,bar')).toBeInTheDocument();
    expect(externalModel.value).toEqual(['foo']);
    await userEvent.click(screen.getByText('OK'));
    expect(externalModel.value).toEqual(['foo', 'bar']);
  });
  describe('hides actions if used from the slot', () => {
    it('nothing', () => {
      render(() => _createVNode(VConfirmEdit, null, null));
      expect(screen.getAllByCSS('button')).toHaveLength(2);
    });
    it('consume model', () => {
      render(() => _createVNode(VConfirmEdit, null, {
        default: _ref3 => {
          let {
            model
          } = _ref3;
          void model;
        }
      }));
      expect(screen.getAllByCSS('button')).toHaveLength(2);
    });
    it('consume actions', () => {
      render(() => _createVNode(VConfirmEdit, null, {
        default: _ref4 => {
          let {
            actions
          } = _ref4;
          void actions;
        }
      }));
      expect(screen.queryAllByCSS('button')).toHaveLength(0);
    });
    it('render actions', () => {
      render(() => _createVNode(VConfirmEdit, null, {
        default: _ref5 => {
          let {
            actions
          } = _ref5;
          return actions();
        }
      }));
      expect(screen.getAllByCSS('button')).toHaveLength(2);
    });
  });
});
//# sourceMappingURL=VConfirmEdit.spec.browser.mjs.map