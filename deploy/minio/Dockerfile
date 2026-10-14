FROM golang:1.24.8 AS build
ENV CGO_ENABLED=0 GOTOOLCHAIN=local GOMAXPROCS=2
RUN go install -p 2 github.com/minio/minio@RELEASE.2025-10-15T17-29-55Z
RUN mkdir -p /test-data
FROM scratch
COPY --from=build /go/bin/minio /minio
COPY --from=build /etc/ssl/certs/ca-certificates.crt /etc/ssl/certs/ca-certificates.crt
COPY --from=build --chown=65532:65532 /test-data /data
USER 65532:65532
ENTRYPOINT ["/minio"]
CMD ["server", "/data"]
