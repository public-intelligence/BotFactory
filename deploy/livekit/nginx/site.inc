# Su dominio y su certificado. Este archivo es suyo: al actualizar Bot Factory
# se reemplaza default.conf, nunca este.
#
# La ruta del certificado es la que ve nginx dentro del contenedor. Por defecto
# el compose monta ./certs en /etc/nginx/certs; si sus archivos viven en otro
# lugar, cambie el montaje en docker-compose.livekit.yml y estas rutas.
#
# Después de editar:  docker compose -f docker-compose.livekit.yml exec nginx nginx -s reload

server_name         voz.sudominio.com;

ssl_certificate     /etc/nginx/certs/fullchain.pem;
ssl_certificate_key /etc/nginx/certs/privkey.pem;
