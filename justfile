default:
    just --list

format:
    dotnet csharpier format .

check:
    dotnet build --no-restore

lint:
    dotnet format --verify-no-changes

test:
    dotnet test

restore:
    dotnet restore

clean:
    dotnet clean
