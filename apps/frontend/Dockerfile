# Stage 1: build Angular
FROM node:22-alpine AS build
WORKDIR /app
COPY package.json package-lock.json .npmrc ./
ARG NPM_TOKEN
RUN npm ci && rm -f .npmrc
COPY . .
RUN npx ng build --configuration production

# Stage 2: serve with nginx
# nginx serves the SPA, falls back to index.html for client-side routes, and
# reverse-proxies /api and /hubs to the API.
#
# The configuration is plain nginx, no templates: the deployment mounts its own
# files over /etc/nginx/conf.d (see deploy/bot_factory/nginx/), where the domain
# and certificate live. The copy baked in here serves plain HTTP and is only
# used when the image runs without those mounts.
# (Production serves the SPA as a static asset and doesn't use this stage.)
FROM nginx:1.27-alpine

COPY nginx/default.conf /etc/nginx/conf.d/default.conf
COPY docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh
RUN chmod +x /usr/local/bin/docker-entrypoint.sh

COPY --from=build /app/dist/frontend/browser /srv

# Written into env.js at start; empty means same-origin / location.origin.
ENV API_URL="" \
    HUBS_URL="" \
    PRODUCT_URL=""

EXPOSE 80 443

ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
CMD ["nginx", "-g", "daemon off;"]
