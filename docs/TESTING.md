# Проверка проекта

```powershell
dotnet test Bitrix24LeadConnector.sln -c Release
node --test tests/frontend.test.cjs
```

Вебхук не требуется. В `ApiFactory` подменяется только `HttpMessageHandler`: API, DI, сервис, валидация и клиент интеграции работают совместно. Настоящие сетевые вызовы CRM в тестах отсутствуют.

## Покрытие ТЗ

| Тест | Проверка | Файл |
|---|---|---|
| T-01 | Mapping заголовка и полей, entityTypeId = 1, fm PHONE / WORK | LeadServiceTests |
| T-02 | Пустое, null, пробельное и короткое имя не доходят до CRM | LeadServiceTests |
| T-03 | Имя длиннее 80 отклоняется, граничная длина принимается | LeadServiceTests |
| T-04 | Телефоны с буквами, лишним плюсом, не-ASCII цифрами и неверной длиной отклоняются | LeadServiceTests |
| T-05 | Пробелы, скобки и дефисы удаляются, начальный + сохраняется | LeadServiceTests |
| T-06 | Комментарий длиннее 1000 отклоняется, 1000 и null принимаются | LeadServiceTests |
| T-07 | Возвращается настоящий ID из ответа подменённого HTTP-транспорта | LeadServiceTests, ApiTests |
| T-08 | HTTP-ошибки API классифицируются, секреты не попадают в ответ/логи | Bitrix24ClientTests, ApiTests |
| T-09 | HTTP 200 с error, включая пустой error и null, считается ошибкой | Bitrix24ClientTests |
| T-10 | Реальный таймер HttpClient отменяет ожидание, повтора нет | Bitrix24ClientTests |
| T-11 | Невалидный JSON и неожиданные структуры не дают ложного успеха | Bitrix24ClientTests |
| T-12 | Отсутствующий, нулевой или отрицательный ID отклоняется | Bitrix24ClientTests |
| T-13 | Упрощённый DTO, дата с часовым поясом, PHONE среди fm | LeadServiceTests |
| T-14 | Пустой массив остаётся пустым | LeadServiceTests, ApiTests |
| T-15 | start передаётся CRM, next становится nextStart | LeadServiceTests |

Дополнительно проверяются отмена клиентом, JSON с неверным типом полей, лимит тела с Content-Length и без него, Content-Type, rate limiting, fail-fast конфигурация, работа `/health` без CRM, Swagger, отсутствие GET и Swagger в Production и Staging. Frontend-тесты проверяют блокировку повторного нажатия, вызов API, отображение ID, ошибки возле полей и сохранение формы при сбое.

На Windows стандартный провайдер логирования может записывать ошибки в EventLog. Если окружение песочницы запрещает такую запись, тесты запускаются в обычном разрешённом окружении пользователя; доступ к настоящему порталу им не нужен.

Для подтверждения интеграции с настоящим Битрикс24 используйте [ручной сценарий](DEMO.md). Автоматические тесты этого не подтверждают.
