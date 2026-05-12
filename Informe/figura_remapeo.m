% ============================================================
%  figura_remapeo.m
%  Versores X_A y Z_A paralelos a los ejes del mundo (TCP
%  solo se traslada, sus versores no rotan).
% ============================================================

clc; clear; close all;

% ── Datos ─────────────────────────────────────────────────────────────────────
A = [0.2,  1.9 ];      % TCP del robot  (KUKA)  → coordenadas (X_mundo, Z_mundo)
B = [2.0,  2.55];      % Main Camera    (MC)

% Versores del TCP paralelos a los ejes del mundo
%   X_A → dirección +X del mundo  = [1, 0] en el plano del plot
%   Z_A → dirección +Z del mundo  = [0, 1] en el plano del plot
X_A = [1, 0];
Z_A = [0, 1];

% Vector C normalizado  (de cámara → TCP, proyectado en XZ)
C_raw = A - B;
C     = C_raw / norm(C_raw);

% ── Ángulos ───────────────────────────────────────────────────────────────────
alpha_ZA = acosd(dot(C, Z_A));
alpha_XA = acosd(dot(C, X_A));

adj_ZA = alpha_ZA; if alpha_ZA > 90, adj_ZA = alpha_ZA - 180; end
adj_XA = alpha_XA; if alpha_XA > 90, adj_XA = alpha_XA - 180; end

% ── Asignación de ejes ────────────────────────────────────────────────────────
% IMPORTANTE: lbl_* sin dólares propios, se envuelven en sprintf
if abs(adj_ZA) <= abs(adj_XA)
    sigma_Z   = ternary(alpha_ZA > 90, -1, +1);
    cross_y   = X_A(1)*C(2) - X_A(2)*C(1);
    sigma_X   = ternary(cross_y >= 0, +1, -1);
    lbl_moveZ = '\hat{Z}_A';
    lbl_moveX = '\hat{X}_A';
else
    sigma_Z   = ternary(alpha_XA > 90, -1, +1);
    cross_y   = Z_A(1)*C(2) - Z_A(2)*C(1);
    sigma_X   = ternary(cross_y >= 0, +1, -1);
    lbl_moveZ = '\hat{X}_A';
    lbl_moveX = '\hat{Z}_A';
end

fprintf('alpha(C, Z_A) = %.2f  adj = %.2f\n', alpha_ZA, adj_ZA);
fprintf('alpha(C, X_A) = %.2f  adj = %.2f\n', alpha_XA, adj_XA);
fprintf('sigma_Z = %+d  |  sigma_X = %+d\n', sigma_Z, sigma_X);

% ── Figura ────────────────────────────────────────────────────────────────────
scale = 0.55;

figure('Color','white','Position',[80 80 780 600]);
hold on; axis equal; grid on;
ax = gca;
ax.GridAlpha            = 0.25;
ax.GridLineStyle        = '--';
ax.FontSize             = 12;
ax.TickLabelInterpreter = 'latex';

% Ejes del mundo (referencia tenue)
quiver(0,0,1.1,0,'Color',[0.78 0.78 0.78],'LineWidth',0.8, ...
       'MaxHeadSize',0.15,'HandleVisibility','off');
quiver(0,0,0,1.1,'Color',[0.78 0.78 0.78],'LineWidth',0.8, ...
       'MaxHeadSize',0.15,'HandleVisibility','off');
text(1.14, 0.00, '$X_{\rm mundo}$','Interpreter','latex', ...
     'FontSize',10,'Color',[0.60 0.60 0.60]);
text(0.03, 1.13, '$Z_{\rm mundo}$','Interpreter','latex', ...
     'FontSize',10,'Color',[0.60 0.60 0.60]);

% Vector C completo  (B → A, línea punteada)
quiver(B(1), B(2), C_raw(1), C_raw(2), 0, ...
       'Color',[0.20 0.20 0.20],'LineWidth',1.8,'MaxHeadSize',0.10, ...
       'LineStyle','-.','DisplayName','$\overrightarrow{C} = A - B$');

% Versor Z_A  (azul, paralelo a +Z mundo)
quiver(A(1), A(2), Z_A(1)*scale, Z_A(2)*scale, 0, ...
       'Color',[0.0 0.45 0.74],'LineWidth',2.8,'MaxHeadSize',0.25, ...
       'DisplayName','$\hat{Z}_A$ (forward TCP $\parallel Z_{\rm mundo}$)');

% Versor X_A  (rojo, paralelo a +X mundo)
quiver(A(1), A(2), X_A(1)*scale, X_A(2)*scale, 0, ...
       'Color',[0.85 0.15 0.10],'LineWidth',2.8,'MaxHeadSize',0.25, ...
       'DisplayName','$\hat{X}_A$ (right TCP $\parallel X_{\rm mundo}$)');

% Puntos
plot(A(1), A(2), 's','MarkerFaceColor',[0.93 0.47 0.0], ...
     'MarkerEdgeColor','k','MarkerSize',11,'DisplayName','KUKA (TCP)');
plot(B(1), B(2), 'o','MarkerFaceColor',[0.47 0.67 0.19], ...
     'MarkerEdgeColor','k','MarkerSize',11,'DisplayName','MC (C\''amara)');

% Etiquetas de los puntos
text(A(1)+0.05, A(2)-0.09, '$A$ (TCP)','Interpreter','latex', ...
     'FontSize',12,'FontWeight','bold','Color',[0.93 0.47 0.0]);
text(B(1)+0.05, B(2)+0.06, '$B$ (MC)','Interpreter','latex', ...
     'FontSize',12,'FontWeight','bold','Color',[0.47 0.67 0.19]);

% Etiqueta ángulo Z_A  (sobre el versor azul)
mid_ZA = A + Z_A*scale*0.60;
text(mid_ZA(1)+0.05, mid_ZA(2), ...
     sprintf('$\\alpha_{Z_A} = %.1f^{\\circ}$', alpha_ZA), ...
     'Interpreter','latex','FontSize',11,'Color',[0.0 0.45 0.74]);

% Etiqueta ángulo X_A  (sobre el versor rojo)
mid_XA = A + X_A*scale*0.55;
text(mid_XA(1)+0.04, mid_XA(2)-0.07, ...
     sprintf('$\\alpha_{X_A} = %.1f^{\\circ}$', alpha_XA), ...
     'Interpreter','latex','FontSize',11,'Color',[0.85 0.15 0.10]);

% ── Recuadro de resultados ────────────────────────────────────────────────────
% Una sola string por línea, los lbl_* NO llevan $ propios;
% el $ exterior los envuelve correctamente.
xlims = [min(A(1),B(1))-0.85,  max(A(1),B(1))+0.85];
ylims = [min(A(2),B(2))-0.90,  max(A(2),B(2))+0.65];

res_x = xlims(2) - 0.80;
res_y = ylims(1) + 0.30;

linea1 = sprintf('$\\mathrm{MoveZ} \\leftarrow %s \\quad (\\sigma_Z = %+d)$', ...
                  lbl_moveZ, sigma_Z);
linea2 = sprintf('$\\mathrm{MoveX} \\leftarrow %s \\quad (\\sigma_X = %+d)$', ...
                  lbl_moveX, sigma_X);

text(res_x, res_y + 0.12, linea1, 'Interpreter','latex','FontSize',11, ...
     'BackgroundColor',[0.97 0.97 0.97],'EdgeColor',[0.50 0.50 0.50], ...
     'Margin',5,'HorizontalAlignment','left');
text(res_x, res_y - 0.06, linea2, 'Interpreter','latex','FontSize',11, ...
     'BackgroundColor',[0.97 0.97 0.97],'EdgeColor',[0.50 0.50 0.50], ...
     'Margin',5,'HorizontalAlignment','left');

% ── Leyenda, ejes, título ─────────────────────────────────────────────────────
legend('Location','northwest','Interpreter','latex','FontSize',11);
xlabel('$X_{\rm mundo}$','Interpreter','latex','FontSize',13);
ylabel('$Z_{\rm mundo}$','Interpreter','latex','FontSize',13);
title({'M\''etodo de remapeo geom\''etrico de ejes del joystick', ...
       'Plano horizontal $XZ$ del mundo'}, ...
      'Interpreter','latex','FontSize',13);

xlim(xlims);
ylim(ylims);
hold off;

% ── Exportar ──────────────────────────────────────────────────────────────────
print('figura_remapeo', '-dpdf', '-vector');
print('figura_remapeo', '-dpng', '-r300');

% ── Helper ────────────────────────────────────────────────────────────────────
function v = ternary(cond, a, b)
    if cond; v = a; else; v = b; end
end