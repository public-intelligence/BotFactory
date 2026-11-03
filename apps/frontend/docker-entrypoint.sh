#!/bin/sh
# Escribe la configuración de la SPA y arranca nginx.
#
# La configuración de nginx ya no se genera aquí: el despliegue monta sus
# archivos sobre /etc/nginx/conf.d y nginx los lee tal cual.
set -e

# window.__env, leído por la SPA (ver src/app/app.config.ts). Vacío = mismo
# origen, que es lo que nginx pasa a la API. productUrl vacío → la SPA usa
# location.origin (correcto en cada despliegue).
cat > /srv/env.js <<INNER
window.__env = { apiUrl: "${API_URL}", hubsUrl: "${HUBS_URL}", productUrl: "${PRODUCT_URL}" };
INNER

exec "$@"
