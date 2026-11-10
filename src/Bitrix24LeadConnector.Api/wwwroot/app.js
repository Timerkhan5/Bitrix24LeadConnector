"use strict";

const form = document.getElementById("lead-form");
const submitButton = document.getElementById("submit-button");
const buttonLabel = document.getElementById("button-label");
const statusBox = document.getElementById("form-status");
const fields = Object.fromEntries(["name", "phone", "comment"].map((key) => [key, document.getElementById(key)]));
let sending = false;

function showStatus(message, kind) {
  statusBox.textContent = message;
  statusBox.dataset.kind = kind;
  statusBox.hidden = false;
}

function showErrors(errors) {
  for (const [key, input] of Object.entries(fields)) {
    const message = errors[key]?.join(" ") ?? "";
    document.getElementById(`${key}-error`).textContent = message;
    input.setAttribute("aria-invalid", String(Boolean(message)));
  }
}

function validate(data) {
  const errors = {};
  if (data.name.length < 2 || data.name.length > 80) {
    errors.name = ["Имя должно содержать от 2 до 80 символов"];
  }
  const digits = data.phone.replace(/[^0-9]/g, "").length;
  if (!/^\+?[0-9 ()-]+$/.test(data.phone) || digits < 10 || digits > 15) {
    errors.phone = ["Введите от 10 до 15 цифр. Допустимы начальный +, пробелы, дефисы и скобки"];
  }
  if (data.comment.length > 1000) {
    errors.comment = ["Комментарий должен содержать не более 1000 символов"];
  }
  return errors;
}

fields.comment.addEventListener("input", () => {
  document.getElementById("comment-count").textContent = `${fields.comment.value.length} / 1000`;
});

form.addEventListener("submit", async (event) => {
  event.preventDefault();
  if (sending) return;

  const data = { name: fields.name.value.trim(), phone: fields.phone.value.trim(), comment: fields.comment.value };
  const errors = validate(data);
  showErrors(errors);
  if (Object.keys(errors).length) {
    showStatus("Проверьте отмеченные поля.", "error");
    fields[Object.keys(errors)[0]].focus();
    return;
  }

  sending = true;
  submitButton.disabled = true;
  form.setAttribute("aria-busy", "true");
  buttonLabel.textContent = "Отправка заявки…";
  showStatus("Отправка заявки…", "loading");

  try {
    const response = await fetch("/api/leads", {
      method: "POST",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify(data),
    });
    const result = await response.json().catch(() => null);
    if (response.status === 201 && result?.success === true && Number.isSafeInteger(result.leadId) && result.leadId > 0) {
      showStatus(`Заявка успешно отправлена! Номер лида в CRM: ${result.leadId}`, "success");
      form.reset();
      document.getElementById("comment-count").textContent = "0 / 1000";
    } else if (response.status === 400 && result?.errors) {
      showErrors(result.errors);
      showStatus("Проверьте данные заявки.", "error");
      const firstInvalid = Object.keys(fields).find((key) => result.errors[key]);
      if (firstInvalid) fields[firstInvalid].focus();
    } else {
      // Use our own messages rather than displaying an arbitrary response body.
      const message = {
        413: "Заявка слишком большая. Сократите введённые данные.",
        429: "Слишком много запросов. Подождите минуту и попробуйте снова.",
        503: "CRM временно недоступна. Уточните результат перед повторной отправкой: заявка могла быть получена.",
        504: "Время ожидания истекло. Заявка могла быть создана — уточните результат перед повторной отправкой.",
      }[response.status] ?? "Не удалось отправить заявку. Попробуйте позже.";
      showStatus(message, "error");
    }
  } catch {
    showStatus("Соединение прервалось. Заявка могла быть получена — уточните результат перед повторной отправкой.", "error");
  } finally {
    sending = false;
    submitButton.disabled = false;
    form.removeAttribute("aria-busy");
    buttonLabel.textContent = "Отправить заявку";
  }
});
