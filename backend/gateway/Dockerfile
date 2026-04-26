FROM node:22-slim
WORKDIR /app

COPY package*.json ./
RUN npm ci

COPY tsconfig.json ./
COPY src ./src
COPY scripts ./scripts
RUN npm run build

RUN npm prune --omit=dev

ENV NODE_ENV=production
EXPOSE 8080
CMD ["node", "--experimental-specifier-resolution=node", "dist/index.js"]
