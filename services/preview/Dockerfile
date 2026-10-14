FROM debian:bookworm-slim
RUN apt-get update && apt-get install -y --no-install-recommends \
    python3 util-linux libreoffice-writer libreoffice-calc libreoffice-impress bubblewrap libseccomp2 \
    fonts-dejavu-core fonts-liberation \
    && rm -rf /var/lib/apt/lists/* \
    && useradd --uid 10001 --create-home converter && mkdir /app /jobs \
    && chown 10001:10001 /jobs
COPY server.py /app/server.py
USER 10001:10001
WORKDIR /app
ENV PYTHONDONTWRITEBYTECODE=1 HOME=/tmp CONVERTER_VERSION=libreoffice-sandbox-v1
EXPOSE 8002
CMD ["python3", "-I", "/app/server.py"]
