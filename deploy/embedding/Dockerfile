FROM python:3.11.14-slim-bookworm
ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1 HF_HUB_OFFLINE=1 TRANSFORMERS_OFFLINE=1 TOKENIZERS_PARALLELISM=false MODEL_DIR=/app/model CPU_THREADS=2
WORKDIR /app
COPY requirements.lock ./
RUN pip install --no-cache-dir --requirement requirements.lock
COPY model/ ./model/
COPY server.py model-manifest.json ./
USER 65532:65532
EXPOSE 8001
CMD ["python", "server.py"]
