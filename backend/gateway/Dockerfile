FROM node:20-slim
WORKDIR /app

COPY package*.json ./
RUN npm ci --omit=dev

COPY tsconfig.json ./
COPY src ./src
COPY scripts ./scripts
RUN npm run build

ENV NODE_ENV=production
EXPOSE 8080
CMD ["node", "--experimental-specifier-resolution=node", "dist/index.js"]
