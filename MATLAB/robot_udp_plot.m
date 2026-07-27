% robot_udp_plot.m
%
% Modulo 2: recibe los angulos articulares por UDP (mismo protocolo que
% udp_joint_receiver.m) y anima el robot en MATLAB usando el Robotics
% Toolbox de Peter Corke (SerialLink), replicando el movimiento visto
% en Unity.
%
% Requiere: Robotics Toolbox de Peter Corke en el path
%   (ej. correr startup_rvc.m si usas la distribucion rvctools).
%
% DH confirmado por Claude Code leyendo directamente los campos _alpha,
% _a, _d, _theta (FrameConfig.cs) del prefab KUKA_KR210_R3100-2.
% Convencion: A = Rz(theta) * Tz(d) * Tx(a) * Rx(alpha) -> DH estandar,
% coincide con el default de Link() de RTB.
%
% FORMULA COMPLETA confirmada por Claude Code (MechanicalUnit.cs,
% HomogeneousMatrix.cs, JointConfig.cs):
%
%   adjusted_deg   = (clamp(JointState.Value[i], limits) + JointConfig.Offset) * JointConfig.Factor
%   theta_final_rad = deg2rad(adjusted_deg) + FrameConfig.Theta
%
% FrameConfig.Theta ya esta cargado como 'offset' de cada Link (DH
% estatico). JointConfig.Offset/Factor es una calibracion mecanica
% APARTE del DH, que hay que aplicar ANTES de convertir a radianes.

clear; clc; close all;

LOCAL_PORT = 25001;

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

% Frame "Flange" (herramienta fija, no es un joint movil): alpha=0, a=0,
% d=0, theta=180 grados -> pura rotacion Rz(pi) montada al final del TCP.
robot.tool = trotz(pi);

%% --- 2) Calibracion mecanica JointConfig (Offset/Factor), confirmada ---
% adjusted_deg = (q_deg_unity + JOINT_CONFIG_OFFSET_DEG) .* JOINT_CONFIG_FACTOR
JOINT_CONFIG_OFFSET_DEG = [0   90  -90   0   0   0];
JOINT_CONFIG_FACTOR     = [1    1    1   1  -1   1];

%% --- 3) Conexion UDP ---
fprintf('Abriendo puerto UDP %d...\n', LOCAL_PORT);
u = udpport("byte", "LocalPort", LOCAL_PORT);
configureTerminator(u, "LF");
flush(u);

%% --- 4) Figura inicial ---
figure('Name', 'KR210 - replica desde Unity');
robot.plot([0 0 0 0 0 0], 'workspace', [-2.5 2.5 -2.5 2.5 0 3.5], 'notiles', 'noname');

fprintf('Escuchando en el puerto %d... (Ctrl+C para detener)\n\n', LOCAL_PORT);

%% --- 5) Loop de recepcion y animacion ---
while true
    if u.NumBytesAvailable > 0
        % Drenamos TODO lo que haya en el buffer y nos quedamos solo con
        % el ultimo paquete: si robot.plot() tarda mas que el intervalo
        % de envio de Unity, se acumulan paquetes viejos en la cola y
        % eso genera delay creciente. Nos interesa el estado actual, no
        % reproducir cada paquete historico.
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
            warning('Paquete mal formado: "%s"', raw);
            continue
        end

        adjusted_deg = (q_deg + JOINT_CONFIG_OFFSET_DEG) .* JOINT_CONFIG_FACTOR;
        q_rad = deg2rad(adjusted_deg);   % el Theta del DH ya esta en el Link

        robot.plot(q_rad);
        drawnow limitrate;
    else
        pause(0.01);
    end
end

%% --- Verificacion sugerida ---
% Con Unity en home (todos los joints en 0), la pose de MATLAB deberia
% verse igual a la pose de home de Unity. Si algo sigue sin coincidir,
% lo mas probable es un limite (clamp) de JointConfig recortando algun
% valor antes de que llegue por UDP -- eso lo tendriamos que confirmar
% con Claude Code (limits_x/limits_y de cada JointConfig).