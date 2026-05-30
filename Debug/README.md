# MR transition debug logs

Logs da transição **MR ↔ VR** gerados no Quest pelo `MRTransitionLog` (`Assets/ramiro/MRTransitionLog.cs`).

## Ficheiros

| Ficheiro | Descrição |
|----------|-----------|
| `mr-transition.log` | Histórico completo (append; rotação ~2 MB) |
| `mr-transition-latest.log` | Cópia sempre igual ao ficheiro acima (mais fácil de puxar) |

## No Quest (origem)

```
/sdcard/Android/data/com.curif.AgeOfJoy/debug/mr-transition.log
/sdcard/Android/data/com.curif.AgeOfJoy/debug/mr-transition-latest.log
```

## Copiar para o PC

```powershell
adb pull /sdcard/Android/data/com.curif.AgeOfJoy/debug/mr-transition-latest.log Debug/
```

Coloque/cole aqui o conteúdo ou substitua estes ficheiros após cada teste.

## O que procurar

- `EnterVRCoroutine` + `DONE` — transição completou
- Último `STEP [...]` antes de parar — onde bloqueou
- `[ERROR]` — reload de cenas falhou
- Muitas linhas `EnterVR requested` seguidas — toggle a disparar em loop (bug de input)
