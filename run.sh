#!/bin/sh
# Запуск API и приложения одной командой
set -e
cd "$(dirname "$0")"

if [ ! -d .venv ]; then
  python3 -m venv .venv
  .venv/bin/pip install -q -r api/requirements.txt
fi

cd api
../.venv/bin/uvicorn main:app --port 8010 &
API_PID=$!
trap 'kill $API_PID' EXIT

cd ..
sleep 5
dotnet run --project app/FilmApp
