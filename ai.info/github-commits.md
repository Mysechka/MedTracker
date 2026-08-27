# GitHub commits — обязательный конфиг для ИИ-агентов

## Пути

| Что | Абсолютный путь |
|---|---|
| Корень репозитория | `/Users/mysechka/RiderProjects/MedTracker` |
| Этот конфиг | `/Users/mysechka/RiderProjects/MedTracker/ai.info/github-commits.md` |
| Промты | `/Users/mysechka/RiderProjects/MedTracker/ai.info/prompts` |
| Постоянные правила | `/Users/mysechka/RiderProjects/MedTracker/ai.info/rules` |

## Репозиторий

- URL: `https://github.com/Mysechka/MedTracker`
- Remote: `origin`
- Ветка по умолчанию: `main`
- Рабочая директория для git: `/Users/mysechka/RiderProjects/MedTracker`

## Автор коммитов

Все `git commit` и `git push` в этом репозитории выполнять **только** от имени:

| Поле | Значение |
|---|---|
| Email | `egorkostin71@gmail.com` |
| Name | `Mysechka` |

Не использовать локальный email машины, anon GitHub noreply и любые другие адреса.

## Как коммитить (агентам)

Перед каждым коммитом задавать автора для этой операции (не через `git config --global`):

```bash
git -c user.email="egorkostin71@gmail.com" -c user.name="Mysechka" commit -m "..."
```

Либо на время сессии:

```bash
export GIT_AUTHOR_EMAIL="egorkostin71@gmail.com"
export GIT_AUTHOR_NAME="Mysechka"
export GIT_COMMITTER_EMAIL="egorkostin71@gmail.com"
export GIT_COMMITTER_NAME="Mysechka"
```

## Правила

1. Коммиты и push — только с email `egorkostin71@gmail.com`.
2. Коммитить и пушить только по явной просьбе пользователя.
3. Не менять глобальный `git config` пользователя.
4. Не пушить force в `main` без явного запроса.
5. Секреты (`.env`, ключи) в коммиты не класть.
