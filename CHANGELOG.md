# Histórico de versões

## 0.4.4
- Reúne as versões de teste 0.4.1 a 0.4.3, que não foram publicadas.
- **Editor de efeitos**: a aba Encantar ganhou o modo **EFEITOS**. Ele mostra os efeitos do item equipado e deixa adicionar, trocar o nível ou remover. O nível vai até 3 no normal e até 10 no livre (acima de 3 é experimental). O menu grava direto no item e o reequipa.
- **Efeitos em artefatos**: no modo EFEITOS, os artefatos equipados entram no fim da lista. Encantamento continua só em armas e armaduras, porque o jogo não deixa encantar artefato.
- **Talismãs**: nova categoria na lista de itens. Cria o talismã no nível escolhido (1 a 3) e muda o nível de um que o herói já tem, com os efeitos de cada nível.
- **Tempestade de Almas**: o botão **IR AO GERADOR** leva o herói ao gerador ativo, mas só funciona com o herói a menos de 40 m dele, quando a barra de vida do gerador aparece. Mais longe, o botão não acha o gerador.
- **Baús da tempestade**: eles ignoram o multiplicador de saque. Com **Saque ×N** ligado, cada equipamento que sai de um deles ganha mais N−1 do mesmo tipo e raridade direto no inventário, com efeitos sorteados de novo. A comida dos baús não é copiada.

## 0.4.0
- Usa o **Blueprint Loader** oficial (Nexus Mods, mod 2), que o jogador instala antes. O pacote traz só o menu; o BetterBlueprintLoader saiu do Nexus e o instalador recusa usá-lo. Quem vem da 0.3.x roda antes o `Remover.ps1` da pasta antiga.
- **Funciona aberto direto pelo Steam.** Antes, sem o `Iniciar-Mod.ps1`, o menu só mostrava os valores, mesmo offline. O iniciador ficou opcional (backup dos saves). Online, cooperativo e sem herói vivo continuam bloqueados.
- **Sem botão Aplicar** em Combate, Experiência e saque, níveis da vila e velocidade do jogo: as chaves e o − / + valem na hora, e um número digitado vale ao apertar Enter ou sair da caixa. Se o jogo recusar, a opção volta ao valor em uso.
- O som do jogo não fica mais abafado com o menu aberto (ele usava os sons do menu de pausa).
- Clicar numa opção não leva mais a página de volta para o topo.

## 0.3.8
- Compatível com o jogo **1.1.3.0** (build 25910487), atualizado pelo Steam em 22/10/2026. O iniciador e o menu passam a aceitar esse executável; o 1.1.2.0 deixa de ser aceito.
- A API do jogo novo foi comparada com tudo o que o menu e o loader usam e nada mudou. O visual, o combate lembrado e os atalhos F5 a F8 foram conferidos em partida.
- Backups de perfil feitos no 1.1.2.0 não são restaurados por esta versão.

## 0.3.7
- O botão do Mercador virou **Renovar loja do Mercador**: renova o estoque pelo próprio jogo, de graça e de qualquer lugar. O botão antigo só enchia um contador e não renovava nada.

## 0.3.6
- A barra de abas mostra os três contadores (fragmentos de eco, esmeraldas, pontos de encantamento) mesmo com sete abas.
- O aviso do F5 diz "Invencibilidade: ligada/desligada".

## 0.3.5
- Botões de hora do dia seguem a divisão do dia do jogo: a primeira metade é dia e a segunda é noite.

## 0.3.4
- Posições salvas passam a gravar o lugar certo (antes ficavam em 0,0,0).

## 0.3.3
- O log fica em `DungeonsMenu_Log.sav`: o Better Blueprint Loader apagava o arquivo antigo a cada fase.
- O combate lembrado volta sozinho depois que o herói morre e renasce.

## 0.3.2
- Nova aba **Mundo**: hora do dia, velocidade do jogo, Tempestade de Almas (experimental), abater, congelar e liberar inimigos num raio, três posições salvas e lista de vagonetas.
- Raridade máxima também nos drops do chão.

## 0.3.1
- Nova aba **Herói**: subir nível, níveis do Encantador, Ferreiro e Mercador, trocas do Mercador, multiplicador de experiência e opções de saque.
- **Remover encantamento** no painel do item equipado.
- O multiplicador de saque soma o bônus quando o valor base do jogo é 0.
- Raridade máxima eleva a Especial os equipamentos que entram no inventário.

## 0.3.0
- Combate extra: flechas, almas e rolagens infinitas, crítico sempre, dano crítico, velocidade de ataque e pulo.
- **Lembrar combate**: o que for aplicado volta sozinho ao trocar de fase ou reabrir o jogo.
- Atalhos F5 a F8 com o menu fechado.

## 0.2.x
- Visual nativo em tela cheia com os estilos do próprio jogo (abas, botões, interruptores, molduras).
- Modo livre de encantamentos, poder e raridade do item equipado, poção sem recarga.

## 0.1.x
- Primeira versão: recursos, itens, mobs, encantamentos e combate; iniciador com verificação da versão do jogo e backup.
