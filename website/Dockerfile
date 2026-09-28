FROM node:22-alpine
WORKDIR /app
COPY server.mjs analytics.mjs survey.mjs packages.mjs compatibility.mjs mirrors.mjs /app/
RUN mkdir -p /app/data /app/public && chown -R node:node /app
USER node
ENV PORT=8080 PUBLIC_ROOT=/app/public DATA_ROOT=/app/data
EXPOSE 8080
CMD ["node", "/app/server.mjs"]
