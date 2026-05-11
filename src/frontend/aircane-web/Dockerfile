# ─────────────────────────────────────────────
# Aircane Web — development image
# Build context: src/frontend/aircane-web
# ─────────────────────────────────────────────
FROM node:20-alpine AS dev

WORKDIR /app

# Copy package files first for layer-cached install
COPY package.json package-lock.json* ./

RUN npm install

# Copy remaining source
COPY . .

EXPOSE 5173

CMD ["npm", "run", "dev", "--", "--host"]
