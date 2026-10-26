# LFS pointer 점검: pull/checkout 후 실제 파일 대신 pointer 텍스트로 남은 LFS 파일을 찾는다.
# Fork의 Custom Command에서 실행한다. 점검만 하고 파일은 받지 않는다 (받기는 `git lfs pull`).
# 종료 코드: 0 = pointer 없음, 1 = pointer 있음

# 저장소 루트(이 스크립트의 상위 폴더)에서 실행한다.
Set-Location (Split-Path -Parent $PSScriptRoot)

# `git lfs ls-files` 출력 형식: "<oid> * <경로>" = 받음, "<oid> - <경로>" = pointer 상태
$pointerLines = git lfs ls-files | Where-Object { $_ -match '^\S+ - ' }
$pointerCount = @($pointerLines).Count

if ($pointerCount -eq 0)
{
    Write-Host "LFS pointer 없음: 모든 LFS 파일이 실제 파일로 받아져 있습니다."
    exit 0
}

Write-Host "LFS pointer $pointerCount 개 발견: 아래 파일이 실제 파일이 아닙니다."
$pointerLines | ForEach-Object { Write-Host ($_ -replace '^\S+ - ', '  ') }
Write-Host ""
Write-Host "해결: Fork에서 'LFS pointer 받기' 실행 (또는 터미널에서 git lfs pull)."
exit 1
