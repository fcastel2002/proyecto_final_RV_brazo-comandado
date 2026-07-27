% udp_joint_receiver.m
%
% Modulo 1: recepcion y visualizacion por consola de los angulos
% articulares enviados desde Unity via UDP.
%
% CONTRATO DE PROTOCOLO (debe coincidir con lo que Claude Code implemente
% del lado de Unity, en el script de broadcasting, ej. JointStateBroadcaster.cs):
%
%   - Transporte: UDP
%   - Un mensaje ASCII por paquete, terminado en '\n' (LF)
%   - Contenido: "q1,q2,q3,q4,q5,q6"  -> 6 valores en GRADOS, separados
%     por coma, en el orden J1..J6 (mismo orden que
%     _controller.MechanicalGroup.JointState.Value en Unity)
%
% Ejemplo de paquete valido:
%   "12.50,-45.00,90.00,0.00,30.00,-15.00\n"
%
% Uso: ajustar LOCAL_PORT segun lo que configure el script de Unity,
% correr este .m, y mover el robot en Unity Play mode. Cada mensaje
% recibido se imprime con timestamp por consola.

clear;
clc;

LOCAL_PORT = 25001;   % debe coincidir con el puerto configurado en Unity

fprintf('Abriendo puerto UDP %d...\n', LOCAL_PORT);
u = udpport("byte", "LocalPort", LOCAL_PORT);
configureTerminator(u, "LF");
flush(u);

% Aseguramos el cierre del puerto aunque se corte la ejecucion (Ctrl+C
% no dispara onCleanup de forma confiable en un while(true) simple, pero
% igual conviene tenerlo si se llama esta rutina desde una funcion).
cleanupObj = onCleanup(@() clear('u')); %#ok<NASGU>

fprintf('Escuchando en el puerto %d... (Ctrl+C para detener)\n\n', LOCAL_PORT);

while true
    if u.NumBytesAvailable > 0
        raw = readline(u);
        raw = strtrim(raw);

        if strlength(raw) == 0
            continue
        end

        parts = strsplit(raw, ",");
        q = str2double(parts);

        if numel(q) ~= 6 || any(isnan(q))
            warning('Paquete mal formado (se esperaban 6 numeros): "%s"', raw);
            continue
        end

        fprintf('[%s] q = [%8.2f %8.2f %8.2f %8.2f %8.2f %8.2f] deg\n', ...
            datestr(now, 'HH:MM:SS.FFF'), q(1), q(2), q(3), q(4), q(5), q(6));
    else
        pause(0.01); % evita busy-wait consumiendo 100% de un core
    end
end