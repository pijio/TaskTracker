# TaskTracker
Информация о созданных мною методах для данной работы:
'public static EventLog CreateLogEntry(EventType logtype, Process process, RunInfo runInfo=null)'
Данный метод создает экземпляр записи для лог-файла. 
'public static string GetExceedingParams(RunInfo runInfo, Process proc)'
Метод возвращающий информацию о типах лимитов, которые могут превысить допустимые значения
'public static bool CheckLimits(RunInfo runInfo, Process proc, LimitType limitType, bool ExceedOrAproach=true)'
Метод проверки лимитов. Логика такая:
если ExceedOrAproach==true тогда проверяем на превышение лимита каждый из типов, либо все
если ExceedOrAproach==false тогда проверяем на приближение к лимиту каждый из типов, либо все
__ Каждый метод принимает контекст процесса __
