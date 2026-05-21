FROM nginx:alpine
# Cài openssl và sinh self-signed cert lúc build image
RUN apk add --no-cache openssl && \
    mkdir -p /etc/nginx/ssl && \
    openssl req -x509 -nodes -days 3650 -newkey rsa:2048 \
        -keyout /etc/nginx/ssl/key.pem \
        -out    /etc/nginx/ssl/cert.pem \
        -subj   '/CN=hdos-platform'
