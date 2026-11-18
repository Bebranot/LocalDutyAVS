# Патчи движка Duty поверх RobustToolbox v294.0.3

Серия перенесена с ветки `duty-perf-lowend` форка `Bebranot/localdutychanges` (база была `f47f6b02b`, ~v277)
на `v294.0.3` (`a4bfa6f4e`). Применять: `git am Tools/engine-patches/*.patch` внутри RobustToolbox.

Перенесено: ACZ >2GB + метаданные aghost, фикс debug-сборки, фикс PVS (aghost + полузаписанный StateStream),
ACZ-блоб на диске и фикс хэндла, режим GC SustainedLowLatency (только оконный клиент).

Не перенесено (апстрим переписал код, перенос без сборки рискован):
- `perf(light)` — кэш CVar/юниформ в Clyde.LightRendering (рендер света сильно переписан);
- `perf(prediction)` — переиспользование буферов в ResetPredictedEntities;
- `perf(clyde)` RT пост-шейдеров — апстрим теперь сам пулит RT (RentPostShaderRenderTarget);
- `perf(audio)` + его откат — взаимно уничтожались.

Ветку нужно запушить в форк (у сессии нет прав на запись в `Bebranot/localdutychanges`), затем вернуть
в `.gitmodules` url форка и поставить пин на запушенный коммит. До тех пор `.gitmodules` указывает на апстрим,
а пин — на стоковый `v294.0.3`.
