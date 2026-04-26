FROM node:20-slim
WORKDIR /app

COPY package*.json ./
# Install ALL deps (including devDeps like typescript) so the build works
RUN npm ci

COPY tsconfig.json ./
COPY src ./src
COPY scripts ./scripts
RUN npm run build

# Prune dev deps after build to keep the image lean
RUN npm prune --omit=dev

ENV NODE_ENV=production
EXPOSE 8080
CMD ["node", "--experimental-specifier-resolution=node", "dist/index.js"]
