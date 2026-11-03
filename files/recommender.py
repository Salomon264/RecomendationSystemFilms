"""Рекомендательная система фильмов (MovieLens).

Три источника рекомендаций:
  * популярность  - взвешенный рейтинг IMDb;
  * SVD           - скрытые факторы пользователей и фильмов (коллаборативная часть);
  * контент       - TF-IDF по жанрам и описанию фильма.
Гибрид = alpha * SVD + (1 - alpha) * контент.

Пример:
    from recommender import Recommender
    model = Recommender.load("recommender.pkl")
    model.recommend({1: 5, 260: 5, 1196: 4.5}, n=10)
"""
import re

import joblib
import numpy as np
import pandas as pd
from scipy.sparse import csr_matrix
from sklearn.decomposition import TruncatedSVD
from sklearn.feature_extraction.text import TfidfVectorizer
from sklearn.preprocessing import normalize


def _minmax(a):
    a = np.asarray(a, dtype=float)
    span = a.max() - a.min()
    return (a - a.min()) / span if span > 0 else np.zeros_like(a)


class Recommender:
    def __init__(self, n_factors=40, alpha=0.7, min_votes=20):
        self.n_factors = n_factors   # число скрытых факторов SVD
        self.alpha = alpha           # вес SVD в гибриде
        self.min_votes = min_votes   # фильмы с меньшим числом оценок не рекомендуем

    # ------------------------------------------------------------ обучение
    def fit(self, movies, ratings, photo=None):
        movies = movies.copy()
        movies["genres"] = movies["genres"].replace("(no genres listed)", "")
        if photo is not None:
            movies = movies.merge(photo[["movieId", "poster_url", "overview"]], on="movieId", how="left")
        for c in ("poster_url", "overview"):
            if c not in movies:
                movies[c] = ""
            movies[c] = movies[c].fillna("")

        # статистика по фильмам + взвешенный рейтинг
        st = ratings.groupby("movieId").rating.agg(votes="count", mean="mean")
        movies = movies.merge(st, on="movieId", how="left")
        movies["votes"] = movies["votes"].fillna(0).astype(int)
        m, c = st.votes.quantile(0.9), st["mean"].mean()
        v, r = movies["votes"], movies["mean"].fillna(c)
        movies["score"] = (v * r + m * c) / (v + m)
        self.movies = movies.set_index("movieId", drop=False)
        self.pos = {mid: i for i, mid in enumerate(self.movies.index)}

        # матрица пользователь x фильм, оценки центрированы по среднему пользователя
        self.user_ids = np.sort(ratings.userId.unique())
        self.uidx = {u: i for i, u in enumerate(self.user_ids)}
        rows = ratings.userId.map(self.uidx).values
        cols = ratings.movieId.map(self.pos).values
        n_items = len(self.movies)
        self.user_mean = ratings.groupby("userId").rating.mean().reindex(self.user_ids).values
        centered = ratings.rating.values - self.user_mean[rows]
        R = csr_matrix((centered, (rows, cols)), shape=(len(self.user_ids), n_items))

        # SVD: факторы фильмов
        svd = TruncatedSVD(self.n_factors, random_state=42).fit(R)
        self.item_f = svd.components_.T                      # фильмы x факторы
        self.item_f_norm = normalize(self.item_f)

        # контент: жанры + описание
        text = (self.movies.genres.str.replace("-", "").str.replace("|", " ", regex=False)
                + " " + self.movies.overview)
        self.tfidf = TfidfVectorizer(stop_words="english", max_features=20000, min_df=2)
        self.X = self.tfidf.fit_transform(text.str.lower())

        self.global_mean = ratings.rating.mean()
        self.ok = (self.movies.votes >= self.min_votes).values   # разрешённые кандидаты
        self.attach_ratings(ratings)
        return self

    # ------------------------------------------------------------ служебное
    def find(self, query):
        """movieId по части названия (берётся самый оцениваемый)."""
        hit = self.movies[self.movies.title.str.contains(re.escape(query), case=False, na=False)]
        if hit.empty:
            raise ValueError(f"Фильм «{query}» не найден")
        return int(hit.sort_values("votes", ascending=False).index[0])

    def _show(self, ids, **extra):
        out = self.movies.loc[ids, ["movieId", "title", "genres", "mean", "votes", "poster_url"]].copy()
        for k, val in extra.items():
            out[k] = val
        return out.rename(columns={"mean": "avg_rating"}).reset_index(drop=True)

    def _user_vector(self, user_ratings):
        vec = np.zeros(len(self.movies))
        for mid, r in user_ratings.items():
            if mid in self.pos:
                vec[self.pos[mid]] = r
        return vec

    # ------------------------------------------------------------ рекомендации
    def popular(self, n=10, genre=None):
        d = self.movies[self.movies.votes >= self.min_votes]
        if genre:
            d = d[d.genres.str.split("|").apply(lambda g: genre in g)]
        ids = d.sort_values("score", ascending=False).head(n).index
        return self._show(ids, score=self.movies.loc[ids, "score"].values)

    def similar(self, movie, n=10, by="svd"):
        """Похожие фильмы: by='svd' (по оценкам) или 'content' (по жанрам и описанию)."""
        mid = movie if isinstance(movie, (int, np.integer)) else self.find(movie)
        i = self.pos[mid]
        if by == "svd":
            sim = self.item_f_norm @ self.item_f_norm[i]
        else:
            sim = (self.X @ self.X[i].T).toarray().ravel()
        sim = np.where(self.ok, sim, -np.inf)
        sim[i] = -np.inf
        top = np.argsort(-sim)[:n]
        ids = self.movies.index[top]
        return self._show(ids, similarity=sim[top].round(3))

    def scores(self, user_ratings, alpha=None):
        """Баллы всех фильмов для пользователя по его оценкам {movieId: rating}."""
        alpha = self.alpha if alpha is None else alpha
        vec = self._user_vector(user_ratings)
        rated = vec > 0
        if not rated.any():
            return self.movies.score.values.copy()
        mean = vec[rated].mean()
        centered = np.where(rated, vec - mean, 0.0)
        svd_part = (centered @ self.item_f) @ self.item_f.T       # "складываем" пользователя в SVD-пространство
        liked = vec >= 4
        if liked.any():
            profile = np.asarray(self.X[liked].mean(axis=0))
            content_part = np.asarray(self.X @ profile.T).ravel()
        else:
            content_part = np.zeros(len(vec))
        return alpha * _minmax(svd_part) + (1 - alpha) * _minmax(content_part)

    def recommend(self, user_ratings, n=10, alpha=None):
        """Рекомендации для любого пользователя (в том числе нового) по его оценкам."""
        s = self.scores(user_ratings, alpha)
        s = np.where(self.ok, s, -np.inf)
        for mid in user_ratings:
            if mid in self.pos:
                s[self.pos[mid]] = -np.inf
        top = np.argsort(-s)[:n]
        return self._show(self.movies.index[top], score=s[top].round(3))

    def recommend_user(self, user_id, n=10, alpha=None):
        """Рекомендации для пользователя, который есть в обучающих данных."""
        return self.recommend(self._ratings_of(user_id), n, alpha)

    # оценки пользователя из обучающей матрицы (восстанавливаем из центрированных значений)
    def _ratings_of(self, user_id):
        i = self.uidx[user_id]
        return self._train_ratings.get(i, {})

    def attach_ratings(self, ratings):
        """Запоминает исходные оценки, чтобы работал recommend_user."""
        self._train_ratings = {}
        for u, g in ratings.groupby("userId"):
            self._train_ratings[self.uidx[u]] = dict(zip(g.movieId, g.rating))
        return self

    # ------------------------------------------------------------ сохранение
    def save(self, path="recommender.pkl"):
        joblib.dump(self, path, compress=3)

    @staticmethod
    def load(path="recommender.pkl"):
        return joblib.load(path)
