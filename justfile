default:
    just --list

format:
    dotnet csharpier format .

check:
    dotnet build --no-restore

lint:
    dotnet format --verify-no-changes

test filter='':
    dotnet test --filter "{{filter}}"

restore:
    dotnet restore

clean:
    dotnet clean
