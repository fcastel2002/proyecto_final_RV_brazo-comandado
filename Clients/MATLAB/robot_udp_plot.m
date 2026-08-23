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

%% --- 2b) Gripper (OnRobot RG2 en el TCP) -- representacion parametrica ---
% El prefab de Unity tiene una jerarquia de escalas FBX anidadas
% (x347.49, x0.001, x999.99994) demasiado inconsistente para reconstruir
% cotas exactas sin abrir el Editor. Lo confirmado es la carrera total
% (stroke) 0-100mm, ver Ctrl_OnRobotRG2_Custom.cs (s_max). Cuerpo y largo
% de dedo son valores representativos, ajustables aca.
% Cotas ajustadas a la ficha tecnica real del RG2 (~195-236mm base-a-dedo);
% antes se veia desproporcionadamente chico (practicamente invisible)
% contra el resto de los eslabones.
GRIPPER_BODY_LENGTH_M = 0.09;
GRIPPER_FINGER_LENGTH_M = 0.11;
GRIPPER_MAX_HALF_STROKE_M = 0.05;  % mitad de los 100mm de carrera total

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
% 'scale' reduce el diametro de los cilindros que dibuja el toolbox para
% los eslabones (default 1); se veian mas "gordos" de lo real y ademas
% tapaban por completo al gripper (mucho mas fino) montado en la muneca.
robot.plot([0 0 0 0 0 0], 'workspace', [-2.5 2.5 -2.5 2.5 0 3.5], 'notiles', 'noname', 'scale', 0.7);

% Panel con los angulos articulares actuales (deg, tal como llegan de
% Unity, antes de aplicar Offset/Factor). 'Units','normalized' lo fija
% a la esquina superior izquierda de los ejes en pantalla sin que rote
% con la camara 3D (misma idea que el panel de udp_robot_viewer.py, ahi
% resuelto con fig.text() de matplotlib).
jointPanel = text(0.02, 0.95, 0, formatJointPanel([0 0 0 0 0 0]), ...
    'Units', 'normalized', 'FontName', 'FixedWidth', 'FontSize', 10, ...
    'VerticalAlignment', 'top', 'Interpreter', 'none');

% Segmentos del gripper (body + 2 dedos), colgados del TCP. Se crean una
% sola vez con line() y se reposicionan en cada paquete via set(), igual
% que jointPanel. NOTA: dentro del loop la variable 'line' se reutiliza
% para leer datagramas (readline), asi que line() como funcion grafica
% solo se llama aca, antes del loop.
hold on;
gripperHandles = [ ...
    line(nan(1, 2), nan(1, 2), nan(1, 2), 'Color', 'k', 'LineWidth', 5), ...
    line(nan(1, 2), nan(1, 2), nan(1, 2), 'Color', 'k', 'LineWidth', 5), ...
    line(nan(1, 2), nan(1, 2), nan(1, 2), 'Color', 'k', 'LineWidth', 5)];

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

        values = str2double(strsplit(raw, ","));
        if ~ismember(numel(values), [6 7]) || any(isnan(values))
            % Puede ser ruido/eco que no es el CSV de articulaciones; se ignora.
            continue
        end

        q_deg = values(1:6);
        if numel(values) == 7
            gripperOpening = values(7);
        else
            % Paquetes viejos (sin 7mo campo) no traen estado de gripper:
            % se asume cerrado, que es el estado inicial real en Unity
            % (GripperController.isGripperClosed arranca en true).
            gripperOpening = 0;
        end

        adjusted_deg = (q_deg + JOINT_CONFIG_OFFSET_DEG) .* JOINT_CONFIG_FACTOR;
        q_rad = deg2rad(adjusted_deg);

        jointPanel.String = formatJointPanel(q_deg);
        robot.plot(q_rad);

        segments = gripperSegments(robot, q_rad, gripperOpening, ...
            GRIPPER_BODY_LENGTH_M, GRIPPER_FINGER_LENGTH_M, GRIPPER_MAX_HALF_STROKE_M);
        for i = 1:numel(gripperHandles)
            p = segments{i};
            set(gripperHandles(i), 'XData', p(1, :), 'YData', p(2, :), 'ZData', p(3, :));
        end

        drawnow limitrate;
    else
        pause(0.01);
    end
end

function lines = formatJointPanel(q_deg)
    lines = {'Articulaciones (deg):'};
    for i = 1:numel(q_deg)
        lines{end + 1} = sprintf('  J%d: %7.2f', i, q_deg(i)); %#ok<AGROW>
    end
end

function segments = gripperSegments(robot, q_rad, openingFraction, bodyLen, fingerLen, maxHalfStroke)
    % Devuelve 3 segmentos (body, dedo izq, dedo der) como matrices 3x2
    % (columnas = puntos mundo), colgando del TCP (mismo frame 'tool' que
    % ya usa robot.tool = trotz(pi)).
    T = robot.fkine(q_rad);
    if isa(T, 'SE3')
        T = T.T;
    end

    halfGap = min(1, max(0, openingFraction)) * maxHalfStroke;

    tcpOrigin = T * [0; 0; 0; 1];
    bodyEnd   = T * [0; 0; bodyLen; 1];
    leftRoot  = T * [-halfGap; 0; bodyLen; 1];
    leftTip   = T * [-halfGap; 0; bodyLen + fingerLen; 1];
    rightRoot = T * [halfGap; 0; bodyLen; 1];
    rightTip  = T * [halfGap; 0; bodyLen + fingerLen; 1];

    segments = { ...
        [tcpOrigin(1:3), bodyEnd(1:3)], ...
        [leftRoot(1:3), leftTip(1:3)], ...
        [rightRoot(1:3), rightTip(1:3)]};
end