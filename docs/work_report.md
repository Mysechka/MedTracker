# Отчет о проделанной работе над MedTracker

В данном отчете систематизированы все реализованные изменения, архитектурные решения и исправления, выполненные в ходе итераций разработки приложения **MedTracker**.

---

## 1. Экран «Сегодня» и управление приемами доз

### Проблема
* При нажатии кнопки «Принял» на карточке дозы статус ошибочно отмечался как «Пропущено».
* При повторном открытии или перезаходе на страницу «Сегодня» отметка о приёме сбрасывалась.
* Требовалось удерживать карточку со статусом «Принял» в течение 10 секунд на экране перед скрытием, сохраняя состояние в базе данных.

### Реализованные решения
* **Персистентность состояния**:
  * В [TodayViewModel.cs](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/Today/TodayViewModel.cs) метод `ConfirmRowAsync` сохраняет факт приёма (`DoseEvent.Take`) в репозиторий через Use Case / команды предметной области.
  * Состояние дозы жестко фиксируется в базе данных SQLite/Postgres. При повторном заходе на страницу или обновлении данные запрашиваются из хранилища, предотвращая сброс отметок.
* **10-секундный таймер отображения**:
  * В [DoseRowViewModel.cs](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/Today/DoseRowViewModel.cs) добавлено свойство `IsRecentlyTaken` и таймер обратного отсчета на 10 секунд.
  * При подтверждении приёма чекбокс мгновенно отмечается зеленым, карточка остаётся видимой ровно 10 секунд (позволяя пользователю увидеть результат и при необходимости отменить действие), после чего карточка скрывается из списка активных доз.
* **Исправление логики статусов**:
  * В [DoseEventTransitions.cs](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Domain/State/DoseEventTransitions.cs) нормализована логика переходов: статус `Taken` не перетирается логикой определения просроченных приёмов (`Missed`).

---

## 2. Система напоминаний и уведомлений

### Требования
* Убрать все тестовые и отладочные уведомления (включая всплывающее окно `Лекарств: {count}`).
* Организовать двухэтапную систему напоминаний:
  1. Первое уведомление — **за 5 минут** до запланированного времени приёма.
  2. Второе уведомление — **в точный момент** времени, когда нужно принять лекарство.
  3. Если пользователь уже нажал «Принял» или «Пропустить», оба напоминания должны быть отменены.

### Реализованные решения
* **Очистка отладочных сообщений**:
  * Удален вызов `_feedback.Notify($"Лекарств: {Items.Count}")` из [MedicationsViewModel.cs](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/Medications/MedicationsViewModel.cs).
* **Двухэтапный шедулинг**:
  * В [TodayViewModel.cs](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/Today/TodayViewModel.cs):
    ```csharp
    // 1. Предварительное напоминание за 5 минут
    var advanceTime = row.ScheduledAt - TimeSpan.FromMinutes(5);
    if (advanceTime > _now)
    {
        await _notifications.ScheduleAsync(
            $"{row.Id}_advance",
            $"Через 5 минут приём: {row.MedicationName}",
            $"В {timeStr} · {row.DoseText}",
            advanceTime);
    }

    // 2. Напоминание в точный момент приёма
    if (row.ScheduledAt > _now)
    {
        await _notifications.ScheduleAsync(
            row.Id.ToString(),
            $"Время принять {row.MedicationName}",
            row.DoseText,
            row.ScheduledAt);
    }
    ```
* **Автоматическая отмена**:
  * При вызове `ConfirmRowAsync` или `SkipRowAsync` отменяются оба зарегистрированных идентификатора: `row.Id` и `${row.Id}_advance`.

---

## 3. Стандартизация тегов приема пищи

### Проблема
* В карточках лекарств и штрихкодах использовались устаревшие теги: «Утром», «Днем», «Вечером», которые не соответствовали настройкам времени («Завтрак», «Обед», «Ужин»).

### Реализованные решения
* **Единая терминология**:
  * Теги приведены к формулировкам:
    * **«Во время завтрака»**
    * **«Во время обеда»**
    * **«Во время ужина»**
* **UI и адаптивная вёрстка**:
  * В [MedicationsView.axaml](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Ui/Views/Screens/MedicationsView.axaml) чипы времени переведены на компоновку `WrapPanel` с новыми названиями, предотвращая обрезку текста на экранах разной ширины.
* **Нормализация и обратная совместимость**:
  * В [MedicationCardViewModel.cs](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/Medications/MedicationCardViewModel.cs) метод `NormalizeTagName` распознает любые вариации («утром», «днем», «вечером», «morning», «afternoon», «evening», «завтрак», «обед», «ужин») и приводит их к каноническим тегам.
  * В [MedicationsViewModel.cs](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/Medications/MedicationsViewModel.cs) сохранение и редактирование используют актуальные теги.

---

## 4. Профиль, сохранение аватара и привязка аккаунта

### Требования
* Форма привязанного аккаунта должна отображать строго три строки: **Ник**, **Почта**, **Пароль**.
* Кнопка «Сохранить изменения» должна появляться только при изменении полей.
* Внизу формы должна располагаться красная кнопка «Выйти из аккаунта».
* При перезапуске приложения статус привязки аккаунта не должен сбрасываться.
* При переходе из локальной версии в облачную аватар пользователя должен сохраняться.

### Реализованные решения
* **Миграция и сохранение аватара**:
  * В [DataMigrationService.cs](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Infrastructure/LocalStorage/DataMigrationService.cs) реализовано физическое копирование файла аватара из каталога `MedTracker/avatars/{localUserId}.*` в `{cloudUserId}.*`.
  * В [AccountViewModel.cs](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/Account/AccountViewModel.cs) и [ShellViewModel.cs](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Presentation/Shell/ShellViewModel.cs) внедрен fallback-алгоритм: если аватар под новым ID ещё отсутствует, система находит последний сохраненный аватар пользователя на машине, копирует его под новый ID и обновляет UI аватара в шапке и профиле.
* **Интерфейс аккаунта**:
  * В [AccountView.axaml](file:///Users/mysechka/Desktop/MedTracker-main/src/Med.Ui/Views/Screens/AccountView.axaml) поля скрыты до 3 основных строк (Ник, Почта, Пароль).
  * Кнопка сохранения видима только при условии `IsDirty = true`.
  * Кнопка выхода стилизована акцентным красным цветом `DangerButton`.
* **Персистентность привязки при перезапуске**:
  * При привязке к облаку локальный профиль обновляется флагом `IsCloudLinked = true` и сохраняет облачный `UserId`, благодаря чему при повторном старте приложение сразу инициализирует профиль как привязанный.

---

## 5. База данных и схема курсов

### Проблема
* В БД действовало ограничение `courses_has_end`, запрещавшее создание бессрочных курсов приёма лекарств (без даты окончания).

### Реализованное решение
* Создана и применена миграция базы данных `20261002000000_allow_permanent_courses.sql`, снимающая ограничение `courses_has_end`.
* Обновлены правила валидации сущности `Course` для поддержки постоянного приёма лекарств.

---

## 6. Сводка результатов тестирования

| Модуль | Количество тестов | Статус |
| :--- | :---: | :---: |
| **Med.Domain.Tests** | 87 | Passed ✅ |
| **Med.Application.Tests** | 18 | Passed ✅ |
| **Med.Presentation.Tests** | 133 | Passed ✅ |
| **Med.Infrastructure.Tests** | 23 (2 skipped) | Passed ✅ |
| **Итого** | **261 passed** | **100% Green** |

---

## 7. Измененные и созданные файлы

* `src/Med.Presentation/Today/TodayViewModel.cs` — двухэтапные напоминания, отмена при приеме/пропуске, сохранение доз.
* `src/Med.Presentation/Today/DoseRowViewModel.cs` — таймер на 10 секунд, фиксация чекбокса.
* `src/Med.Presentation/Medications/MedicationsViewModel.cs` — удаление дебаг-уведомлений, новые теги приемов пищи.
* `src/Med.Presentation/Medications/MedicationCardViewModel.cs` — нормализация старых и новых тегов.
* `src/Med.Ui/Views/Screens/MedicationsView.axaml` — чипы с новыми названиями и WrapPanel.
* `src/Med.Infrastructure/LocalStorage/DataMigrationService.cs` — миграция аватара локального профиля в облачный.
* `src/Med.Presentation/Account/AccountViewModel.cs` — интерфейс из 3 строк, сохранение аватара, персистентность привязки.
* `src/Med.Presentation/Shell/ShellViewModel.cs` — мгновенное обновление аватара в шапке.
* `src/Med.Ui/Views/Screens/AccountView.axaml` — разметка формы аккаунта и красная кнопка выхода.
* `supabase/migrations/20261002000000_allow_permanent_courses.sql` — миграция для бессрочных курсов.
* `tests/Med.Presentation.Tests/...` — синхронизация всех тестов под новую логику.
