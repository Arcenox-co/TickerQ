### To have in consideration if something goes wrong with this sample project

- One of the errors that you might have is that the dashboard path gives you a 404. In this case, make that you firsst build the dashboard project.

Fixing instructions:
1. Make sure your terminal is located in the dashboard `wwwroot` path.
2. `npm install`
3. `npm run build`


- Another problem that you might have is related to EF migrations missing/out of date.
Fixing instructions:
1. Make sure your terminal is located in the dashboard `.WebApi` path.
2. `dotnet ef migrations add <migration-name>`. This makes sure that you have the latest version.
3. `dotnet ef database update`. This makes sure that the latest migration you've added is applied.