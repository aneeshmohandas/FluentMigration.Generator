# FluentMigration.Generator

## Install 
```
dotnet tool install --global FluentMigration.Generator.PostgreSql --version 1.0.0
```
## RUN
``` 
add-migration --c "Database Connection String" --a "Assembly Location" 
```
## Example
### Model
```
[Table("Fruit", Schema = "public")] //required
public class Fruit
{
    public Guid Id { get; set; }
    public string Name { get; set; }
}
```
### Output
```
using FluentMigrator;
using System;
namespace MigrationGenerator.PostgreSql.Migrations
{
    [Migration(2022213193819558)] // created from timestamp
    public class Migration_2022213193819558 : Migration
    {
        public override void Up()
        {
            Create.Table("fruit").InSchema("public")
              .WithColumn("id").AsGuid().NotNullable().PrimaryKey()
              .WithColumn("name").AsString(100).NotNullable();
        }
        public override void Down()
        {
            Delete.Table("fruit").InSchema("public");
        }
    }
}
```


