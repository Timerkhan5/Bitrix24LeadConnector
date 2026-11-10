const { test } = require("node:test");
const assert = require("node:assert/strict");
const { readFileSync } = require("node:fs");
const { resolve } = require("node:path");
const vm = require("node:vm");
const source = readFileSync(resolve(__dirname, "../src/Bitrix24LeadConnector.Api/wwwroot/app.js"), "utf8");

function browser(fetch) {
  const elements = {};
  const ids = ["lead-form", "submit-button", "button-label", "form-status", "name", "phone", "comment", "name-error", "phone-error", "comment-error", "comment-count"];
  for (const id of ids) elements[id] = {
    value: "", textContent: "", dataset: {}, attrs: {}, events: {}, disabled: false,
    addEventListener(event, fn) { this.events[event] = fn; },
    setAttribute(key, value) { this.attrs[key] = value; },
    removeAttribute(key) { delete this.attrs[key]; },
    focus() { this.focused = true; },
  };
  elements["lead-form"].reset = () => { for (const id of ["name", "phone", "comment"]) elements[id].value = ""; };
  elements.name.value = " Тестовый клиент ";
  elements.phone.value = "+7 (000) 000-00-00";
  elements.comment.value = "Тестовая заявка";
  vm.runInNewContext(source, { document: { getElementById: (id) => elements[id] }, fetch });
  return { elements, submit: () => elements["lead-form"].events.submit({ preventDefault() {} }) };
}

test("valid submission is sent once, locks the button, and displays the CRM id", async () => {
  let resolveFetch;
  let calls = 0;
  const ui = browser((url, options) => {
    calls++;
    assert.equal(url, "/api/leads");
    assert.equal(JSON.parse(options.body).name, "Тестовый клиент");
    return new Promise((resolveResponse) => { resolveFetch = resolveResponse; });
  });
  const pending = ui.submit();
  assert.equal(ui.elements["submit-button"].disabled, true);
  await ui.submit();
  assert.equal(calls, 1);
  resolveFetch({ status: 201, json: async () => ({ success: true, leadId: 123 }) });
  await pending;
  assert.match(ui.elements["form-status"].textContent, /123/);
  assert.equal(ui.elements["form-status"].dataset.kind, "success");
  assert.equal(ui.elements["submit-button"].disabled, false);
  assert.equal(ui.elements.name.value, "");
});

test("invalid fields prevent the HTTP request and show inline errors", async () => {
  let calls = 0;
  const ui = browser(() => { calls++; });
  ui.elements.phone.value = "7+0000000000";
  await ui.submit();
  assert.equal(calls, 0);
  assert.equal(ui.elements.phone.attrs["aria-invalid"], "true");
  assert.ok(ui.elements["phone-error"].textContent);
  assert.equal(ui.elements.phone.focused, true);
});

for (const status of [400, 429, 502, 503, 504]) {
  test(`HTTP ${status} preserves the form and enables another deliberate attempt`, async () => {
    const ui = browser(async () => ({ status, json: async () => ({ errors: status === 400 ? { phone: ["Ошибка телефона"] } : undefined }) }));
    await ui.submit();
    assert.equal(ui.elements.comment.value, "Тестовая заявка");
    assert.equal(ui.elements["submit-button"].disabled, false);
    assert.equal(ui.elements["form-status"].dataset.kind, "error");
    if (status === 504) assert.match(ui.elements["form-status"].textContent, /могла быть создана/);
  });
}

test("network error does not retry or erase the form", async () => {
  let calls = 0;
  const ui = browser(async () => { calls++; throw new Error("network"); });
  await ui.submit();
  assert.equal(calls, 1);
  assert.equal(ui.elements.comment.value, "Тестовая заявка");
  assert.match(ui.elements["form-status"].textContent, /могла быть получена/);
});
