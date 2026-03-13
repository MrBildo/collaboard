$backend = Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd backend/Collaboard.Api; dotnet run" -PassThru
$frontend = Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd frontend; npm install --silent; npm run dev" -PassThru

try {
    Write-Host "Backend PID: $($backend.Id) | Frontend PID: $($frontend.Id)"
    Write-Host "Press Ctrl+C to stop both..."
    Wait-Process -Id $backend.Id, $frontend.Id
} finally {
    $backend, $frontend | Where-Object { !$_.HasExited } | Stop-Process -Force
}
