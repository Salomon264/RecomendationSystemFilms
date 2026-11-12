# Рекомендации фильмов (Avalonia + FastAPI)

Готовый `recommender.pkl` лежит в `files/`, рядом с ним `recommender.py` (нужен при загрузке модели).

## Запуск

```bash
./run.sh          # поднимет API на 8010 и откроет приложение
```

Или вручную:

```bash
python3 -m venv .venv
.venv/bin/pip install -r api/requirements.txt
cd api && ../.venv/bin/uvicorn main:app --port 8010
dotnet run --project app/FilmApp
```

## API (http://127.0.0.1:8010)

| Метод | Назначение |
|---|---|
| `GET /popular?n=10&genre=Comedy` | топ популярных (взвешенный рейтинг) |
| `GET /similar?title=Toy Story&n=10&by=svd` | похожие: `svd` (коллаборативно) или `content` (жанры+описание) |
| `GET /recommend?movie_ids=1,260&ratings=5,4&n=10` | рекомендации по оценкам (гибрид SVD + контент) |
| `GET /users/{id}/recommendations?n=10` | персональные рекомендации для пользователя из датасета |
| `GET /movies?query=star&limit=20` | поиск фильмов (с годом и постером) |
| `GET /movies/{id}` | карточка фильма: описание, постер, оценки, балл модели |
| `POST /ratings` `{user_id, movie_id, rating}` | сохранить оценку в SQLite (`api/ratings.db`) |
| `GET /ratings/{user_id}` | оценки пользователя из БД |
| `GET /ratings/{user_id}/recommendations?n=10` | рекомендации по сохранённым в БД оценкам |
| `GET /help` | справка по командам |

## Приложение

Avalonia (`app/FilmApp`): тёмная тема, список результатов слева с постерами, справа — карточка фильма с описанием, жанрами и метриками. Кнопки — топ, похожие (SVD/контент), рекомендации по сохранённым оценкам, мои оценки, справка. Оценка выбранного фильма сохраняется в SQLite через API.

Постеры грузятся с TMDB; если домен недоступен, тот же кадр запрашивается через прокси `wsrv.nl`.

## Ноутбук

`files/film-recommendation-system.ipynb` — анализ данных (пропуски, годы, жанры, активность), обучение, проверка качества и сохранение модели. Данные лежат в `files/` (`movies.csv`, `ratings (1).csv`, `photo.csv`). Для графиков нужен matplotlib:

```bash
.venv/bin/pip install matplotlib
```
