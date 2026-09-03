EF Core migrations are intentionally generated from `NlpDbContext` in the deployment environment:

```bash
dotnet ef migrations add InitialNlpSchema --project src/Olga.Nlp.Infrastructure --startup-project src/Olga.Nlp.Api
dotnet ef database update --project src/Olga.Nlp.Infrastructure --startup-project src/Olga.Nlp.Api
```

The checked-in SQL scripts are the repeatable deployment path for the stored procedures and the schema baseline. Run them in order, then deploy the API. Production migrations should be reviewed and applied by the release pipeline with a rollback plan.
