"""Простое API рекомендательной системы фильмов (FastAPI + SQLite)."""
import sqlite3
from contextlib import asynccontextmanager
from pathlib import Path

import joblib
import pandas as pd
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field

BASE = Path(__file__).parent
DB = BASE / "ratings.db"
MODEL = BASE / "recommender.pkl"
if not MODEL.exists():  
    MODEL = BASE.parent / "files" / "recommender.pkl"
model = None


def db():
    conn = sqlite3.connect(DB)
    conn.execute(
        """CREATE TABLE IF NOT EXISTS ratings (
               user_id INTEGER, movie_id INTEGER, rating REAL,
               PRIMARY KEY (user_id, movie_id))"""
    )
    return conn


@asynccontextmanager
async def lifespan(_):
    global model
    model = joblib.load(MODEL)
    db().close()
    yield


app = FastAPI(title="Film Recommender API", version="1.0", lifespan=lifespan)


def frame(df):
    """DataFrame -> список словарей для JSON."""
    return df.fillna("").to_dict(orient="records")


class Rating(BaseModel):
    user_id: int = Field(ge=1)
    movie_id: int
    rating: float = Field(ge=0.5, le=5)


@app.get("/help")
def help_():
    return {
        "popular": "GET /popular?n=10&genre=Comedy — топ популярных фильмов",
        "similar": "GET /similar?title=Toy Story&n=10&by=svd|content — похожие фильмы",
        "recommend": "GET /recommend?movie_ids=1,260&ratings=5,4&n=10 — рекомендации по оценкам",
        "user": "GET /users/{user_id}/recommendations?n=10 — персональные рекомендации",
        "search": "GET /movies?query=star&limit=20 — поиск фильмов",
        "ratings": "POST /ratings {user_id, movie_id, rating} — сохранить оценку; "
                   "GET /ratings/{user_id} — оценки пользователя",
    }


@app.get("/movies")
def movies(query: str = "", limit: int = 20):
    d = model.movies
    if query:
        d = d[d.title.str.contains(query, case=False, na=False)]
    d = d.sort_values("votes", ascending=False).head(limit)
    return frame(d[["movieId", "title", "year", "genres", "mean", "votes", "poster_url"]])


@app.get("/movies/{movie_id}")
def movie_details(movie_id: int):
    if movie_id not in model.pos:
        raise HTTPException(404, f"фильм {movie_id} не найден")
    row = model.movies.loc[movie_id]
    return {
        "movieId": int(row.movieId),
        "title": row.title,
        "year": None if pd.isna(row.year) else int(row.year),
        "genres": row.genres,
        "mean": None if pd.isna(row["mean"]) else float(row["mean"]),
        "votes": int(row.votes),
        "poster_url": row.poster_url or None,
        "overview": row.overview or None,
        "score": float(row.score),
    }


@app.get("/popular")
def popular(n: int = 10, genre: str | None = None):
    return frame(model.popular(n, genre))


@app.get("/similar")
def similar(title: str, n: int = 10, by: str = "svd"):
    if by not in ("svd", "content"):
        raise HTTPException(400, "by должен быть svd или content")
    try:
        return frame(model.similar(title, n, by))
    except ValueError as e:
        raise HTTPException(404, str(e))


@app.get("/recommend")
def recommend(movie_ids: str = "", ratings: str = "", n: int = 10):
    ids = [int(x) for x in movie_ids.split(",") if x.strip()]
    vals = [float(x) for x in ratings.split(",") if x.strip()]
    if not ids:
        raise HTTPException(400, "нужен хотя бы один movie_ids")
    if not vals:
        vals = [5.0] * len(ids)
    if len(vals) != len(ids):
        raise HTTPException(400, "количество movie_ids и ratings должно совпадать")
    return frame(model.recommend(dict(zip(ids, vals)), n))


@app.get("/users/{user_id}/recommendations")
def user_recommendations(user_id: int, n: int = 10):
    if user_id not in model.uidx:
        raise HTTPException(404, f"пользователь {user_id} не найден")
    return frame(model.recommend_user(user_id, n))


@app.get("/users")
def users(limit: int = 20):
    return [int(u) for u in model.user_ids[:limit]]


@app.post("/ratings")
def save_rating(r: Rating):
    if r.movie_id not in model.pos:
        raise HTTPException(404, f"фильм {r.movie_id} не найден")
    conn = db()
    conn.execute("INSERT OR REPLACE INTO ratings VALUES (?, ?, ?)",
                 (r.user_id, r.movie_id, r.rating))
    conn.commit()
    conn.close()
    return {"ok": True}


@app.get("/ratings/{user_id}")
def get_ratings(user_id: int):
    conn = db()
    rows = conn.execute("SELECT movie_id, rating FROM ratings WHERE user_id = ?",
                        (user_id,)).fetchall()
    conn.close()
    titles = model.movies.title
    posters = model.movies.poster_url
    return [{"movie_id": m, "title": titles.get(m, ""), "rating": r, "poster_url": posters.get(m, "")}
            for m, r in rows]


@app.get("/ratings/{user_id}/recommendations")
def saved_recommendations(user_id: int, n: int = 10):
    """Рекомендации по оценкам, сохранённым в БД."""
    conn = db()
    rows = conn.execute("SELECT movie_id, rating FROM ratings WHERE user_id = ?",
                        (user_id,)).fetchall()
    conn.close()
    if not rows:
        raise HTTPException(404, "у пользователя нет сохранённых оценок")
    return frame(model.recommend({m: r for m, r in rows}, n))
