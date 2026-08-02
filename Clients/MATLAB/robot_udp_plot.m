% robot_udp_plot.m
%
% Modulo 2: cliente UDP con suscripcion dinamica al servidor de Unity
% (JointStateBroadcaster.cs, arquitectura de servidor con suscripcion).
% Recibe los angulos articulares y anima el robot en MATLAB usando el
% Robotics Toolbox de Peter Corke (SerialLink), replicando el movimiento
% visto en Unity.
%
% ARQUITECTURA (Opcion B - servidor con suscripcion dinamica):
%   - Unity escucha en un puerto fijo (_serverPort) y YA NO tiene un
%     destino fijo configurado en el Inspector. Cualquier cliente que le
%     mande un datagrama a ese puerto queda registrado como suscriptor y
%     empieza a recibir el estado articular.
%   - Este script pregunta por consola la IP/puerto de Unity, manda un
%     datagrama de suscripcion ("HELLO"), lo repite periodicamente como
%     keep-alive, y escucha en el mismo socket las respuestas de Unity.
%
% Requiere: Robotics Toolbox de Peter Corke en el path.
%
% DH y formula de conversion: ver comentarios de la version anterior /
% _ARQUITECTURA_CONTROL.md. Resumen:
%   adjusted_deg    = (JointState.Value[i] + JointConfig.Offset) * JointConfig.Factor
%   theta_final_rad = deg2rad(adjusted_deg) + FrameConfig.Theta   (Theta va en 'offset' del Link)

clear; clc; close all;

KEEPALIVE_INTERVAL_S = 5;

%% --- 0) Preguntar IP y puerto del servidor Unity ---
targetIp = strtrim(input('IP de la aplicacion Unity (servidor UDP): ', 's'));
targetPort = [];
while isempty(targetPort) || isnan(targetPort)
    targetPortStr = strtrim(input('Puerto UDP del servidor Unity: ', 's'));
    targetPort = str2double(targetPortStr);
    if isnan(targetPort)
        fprintf('Puerto invalido, ingresa un numero entero.\n');
    end
end

%% --- 1) Definicion del robot (DH confirmado) ---
% Columnas: [alpha(rad)  a(m)  d(m)  theta_offset_DH(rad)]
DH_PARAMS = [ ...
    -pi/2   0.33    0.645   0     ;   % Joint_1
     0      1.35    0       -pi/2 ;   % Joint_2
    -pi/2   0.115   0       0     ;   % Joint_3
    -pi/2   0       1.42    0     ;   % Joint_4
     pi/2   0       0       0     ;   % Joint_5
     0      0       0.24    0     ]; % Joint_6

clear L
for i = 1:6
    L(i) = Link('revolute', ...
                 'alpha',  DH_PARAMS(i,1), ...
                 'a',      DH_PARAMS(i,2), ...
                 'd',      DH_PARAMS(i,3), ...
                 'offset', DH_PARAMS(i,4));
end
robot = SerialLink(L, 'name', 'KR210_sim');
robot.tool = trotz(pi);   % frame "Flange": rotacion fija de 180 grados

%% --- 2) Calibracion mecanica JointConfig (Offset/Factor), confirmada ---
JOINT_CONFIG_OFFSET_DEG = [0   90  -90   0   0   0];
JOINT_CONFIG_FACTOR     = [1    1    1   1  -1   1];

%% --- 3) Conexion UDP (puerto local automatico, NO fijo) ---
% No especificamos LocalPort: el SO asigna uno efimero. Unity le responde
% al RemoteEndPoint de quien mando el datagrama de suscripcion, asi que
% no importa cual sea nuestro puerto local.
u = udpport("byte");
flush(u);

fprintf('Suscribiendose a %s:%d ...\n', targetIp, targetPort);
write(u, uint8('HELLO'), targetIp, targetPort);
lastKeepAlive = tic;

%% --- 4) Figura inicial ---
figure('Name', 'KR210 - replica desde Unity');
robot.plot([0 0 0 0 0 0], 'workspace', [-2.5 2.5 -2.5 2.5 0 3.5], 'notiles', 'noname');

fprintf('Escuchando... (Ctrl+C para detener)\n\n');

%% --- 5) Loop de recepcion, keep-alive y animacion ---
while true
    % Keep-alive: re-suscribirse periodicamente por si Unity tiene
    % habilitada la limpieza automatica de suscriptores inactivos.
    if toc(lastKeepAlive) >= KEEPALIVE_INTERVAL_S
        write(u, uint8('HELLO'), targetIp, targetPort);
        lastKeepAlive = tic;
    end

    if u.NumBytesAvailable > 0
        % Nos quedamos solo con el ultimo paquete disponible para evitar
        % el delay acumulado si robot.plot() tarda mas que el intervalo
        % de envio de Unity.
        raw = "";
        while u.NumBytesAvailable > 0
            line = readline(u);
            if strlength(strtrim(line)) > 0
                raw = line;
            end
        end
        raw = strtrim(raw);

        if strlength(raw) == 0
            continue
        end

        q_deg = str2double(strsplit(raw, ","));
        if numel(q_deg) ~= 6 || any(isnan(q_deg))
            % Puede ser ruido/eco que no es el CSV de articulaciones; se ignora.
            continue
        end

        adjusted_deg = (q_deg + JOINT_CONFIG_OFFSET_DEG) .* JOINT_CONFIG_FACTOR;
        q_rad = deg2rad(adjusted_deg);

        robot.plot(q_rad);
        drawnow limitrate;
    else
        pause(0.01);
    end
end