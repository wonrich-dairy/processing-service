### 2. Start MySQL (isolated datastore - SCRUM-71)
```bash
docker compose up -d mysql
# or
docker run -d --name wonrich-mysql -e MYSQL_ROOT_PASSWORD=RootPassword123! -e MYSQL_DATABASE=processing -e MYSQL_USER=processing_user -e MYSQL_PASSWORD=DevPassword123! -p 3308:3306 mysql:8.4
```

### 3. Apply migrations (Pomelo - SCRUM-57)
```bash
dotnet tool restore
dotnet ef database update --project src/ProcessingService
```

### 4. Run service
```bash
dotnet run --project src/ProcessingService --environment Development
```

- Health: http://localhost:5210/health (anonymous, 200 + DB healthy)
- Swagger: http://localhost:5210/swagger (Development/Staging only)
- Metrics: http://localhost:5210/metrics (Prometheus, when SCRUM-90 merged)

### 5. Run tests
```bash
dotnet test
```